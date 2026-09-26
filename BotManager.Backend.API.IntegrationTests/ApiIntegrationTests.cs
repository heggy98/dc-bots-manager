using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BotManager.Backend.API.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ApiIntegrationTests
{
    private readonly ApiFactory _factory;

    public ApiIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ReportsHealthy()
    {
        var client = _factory.CreateClientFrom("10.1.0.1");

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ApiResponses_CarrySecurityHeaders()
    {
        var client = _factory.CreateClientFrom("10.1.0.2");

        var response = await client.GetAsync("/api/bot/public");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task PublicBots_DoNotExposeOwner()
    {
        var client = _factory.CreateClientFrom("10.1.0.3");

        var json = await client.GetStringAsync("/api/bot/public");

        Assert.Contains("\"botId\"", json);
        Assert.DoesNotContain("ownerUserId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ApiFactory.AdminEmail, json);
    }

    [Fact]
    public async Task Startup_ReprotectsLegacyPlainTextTokens()
    {
        await using var db = _factory.CreateDbContext();

        var bot = await db.Bots.AsNoTracking().SingleAsync(b => b.BotId == ApiFactory.LegacyBotId);

        Assert.StartsWith("v1:", bot.BotToken);
        Assert.DoesNotContain(ApiFactory.LegacyPlainToken, bot.BotToken);
    }

    [Fact]
    public async Task Login_SetsHttpOnlyCookies_AndReturnsNoToken()
    {
        var client = _factory.CreateClientFrom("10.2.0.1");

        var response = await LoginAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.StartsWith("bm_access=") && c.Contains("httponly") && c.Contains("samesite=strict"));
        Assert.Contains(cookies, c => c.StartsWith("bm_refresh=") && c.Contains("path=/api/auth") && c.Contains("httponly"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("token\"", body.Replace("TokenExpiresAt\"", string.Empty), StringComparison.OrdinalIgnoreCase);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(ApiFactory.AdminEmail, me.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Login_RejectsMissingAndWrongCredentials()
    {
        var client = _factory.CreateClientFrom("10.2.0.2");

        var empty = await client.PostAsJsonAsync("/api/auth/login", new { });
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = ApiFactory.AdminEmail, password = "nope" });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoints_RequireAuthentication()
    {
        var client = _factory.CreateClientFrom("10.2.0.3");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/bot/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync("/hubs/bot-events/negotiate?negotiateVersion=1", null)).StatusCode);
    }

    [Fact]
    public async Task Bruteforce_LocksOutAfterConfiguredFailures()
    {
        var client = _factory.CreateClientFrom("10.3.0.1");

        for (var i = 0; i < 5; i++)
        {
            var failed = await client.PostAsJsonAsync("/api/auth/login", new { email = "x@example.com", password = "nope" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var locked = await LoginAsync(client);
        Assert.Equal((HttpStatusCode)429, locked.StatusCode);
    }

    [Fact]
    public async Task CookieAuthenticatedWrites_RequireCsrfHeader()
    {
        var client = _factory.CreateClientFrom("10.4.0.1");
        await LoginAsync(client);

        var withoutHeader = await client.PutAsJsonAsync($"/api/bot/admin/{ApiFactory.LegacyBotId}/visibility", new { isPublic = true });

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/bot/admin/{ApiFactory.LegacyBotId}/visibility")
        {
            Content = JsonContent.Create(new { isPublic = true })
        };
        request.Headers.Add("X-Requested-With", "BotManager");
        var withHeader = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, withoutHeader.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withHeader.StatusCode);
    }

    [Fact]
    public async Task Hub_RejectsForeignOrigin()
    {
        var client = _factory.CreateClientFrom("10.4.0.2");
        await LoginAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/bot-events/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", "https://evil.example");
        var foreign = await client.SendAsync(request);

        var allowed = await client.PostAsync("/hubs/bot-events/negotiate?negotiateVersion=1", null);

        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndDetectsReuse()
    {
        var client = _factory.CreateClientFrom("10.5.0.1");
        var login = await LoginAsync(client);
        var originalRefresh = ExtractCookie(login, "bm_refresh");

        var refreshed = await PostWithCsrfAsync(client, "/api/auth/refresh");
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.NotEqual(originalRefresh, ExtractCookie(refreshed, "bm_refresh"));

        // An attacker replays the original (rotated) refresh token.
        var attacker = _factory.CreateClientFrom("10.5.0.2");
        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        replay.Headers.Add("Cookie", $"bm_refresh={originalRefresh}");
        replay.Headers.Add("X-Requested-With", "BotManager");
        Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.SendAsync(replay)).StatusCode);

        // Reuse revoked the whole family: the legitimate client's current refresh token is dead too.
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostWithCsrfAsync(client, "/api/auth/refresh")).StatusCode);
    }

    [Fact]
    public async Task LogoutAll_InvalidatesIssuedAccessTokens()
    {
        var device1 = _factory.CreateClientFrom("10.6.0.1");
        var device2 = _factory.CreateClientFrom("10.6.0.2");
        await LoginAsync(device1);
        await LoginAsync(device2);
        Assert.Equal(HttpStatusCode.OK, (await device2.GetAsync("/api/auth/me")).StatusCode);

        // Tokens carry second-resolution iat; make sure the watermark is strictly after them.
        await Task.Delay(1100);
        Assert.Equal(HttpStatusCode.NoContent, (await PostWithCsrfAsync(device1, "/api/auth/logout-all")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await device2.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostWithCsrfAsync(device2, "/api/auth/refresh")).StatusCode);

        // A fresh login works again afterwards.
        await Task.Delay(1100);
        var device3 = _factory.CreateClientFrom("10.6.0.3");
        await LoginAsync(device3);
        Assert.Equal(HttpStatusCode.OK, (await device3.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Logs_ArePersistedToSystemLogs_WithBotId()
    {
        var client = _factory.CreateClientFrom("10.7.0.1");
        await LoginAsync(client);

        // Warning-level event (always persisted).
        var marker = $"marker-{Guid.NewGuid():N}@example.com";
        // (Separate client: a cookie-carrying client would need the CSRF header even for login.)
        await _factory.CreateClientFrom("10.7.0.2").PostAsJsonAsync("/api/auth/login", new { email = marker, password = "nope" });

        // Bot-scoped Information event (persisted because it carries BotId).
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/bot/admin/{ApiFactory.LegacyBotId}/autostart")
        {
            Content = JsonContent.Create(new { autoStart = false })
        };
        request.Headers.Add("X-Requested-With", "BotManager");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);

        SystemLog? warning = null;
        SystemLog? botScoped = null;
        for (var attempt = 0; attempt < 30 && (warning == null || botScoped == null); attempt++)
        {
            await Task.Delay(500);
            await using var db = _factory.CreateDbContext();
            warning ??= await db.SystemLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Message.Contains(marker));
            botScoped ??= await db.SystemLogs.AsNoTracking()
                .FirstOrDefaultAsync(l => l.BotId == ApiFactory.LegacyBotId && l.Message.Contains("auto-start updated"));
        }

        await using (var db = _factory.CreateDbContext())
        {
            var recent = await db.SystemLogs.AsNoTracking().OrderByDescending(l => l.Id).Take(8)
                .Select(l => l.Level + "|" + l.BotId + "|" + l.Message).ToListAsync();
            Assert.True(warning != null, "Warning log missing. Recent: " + string.Join(" || ", recent));
            Assert.True(botScoped != null, "Bot-scoped log missing. Recent: " + string.Join(" || ", recent));
        }

        Assert.Equal("Warning", warning!.Level);
        Assert.False(string.IsNullOrEmpty(warning.Category));
        Assert.NotNull(botScoped);
    }

    [Fact]
    public async Task AdminActions_AreWrittenToAdminAuditLog()
    {
        var client = _factory.CreateClientFrom("10.8.0.1");
        await LoginAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/bot/admin/{ApiFactory.LegacyBotId}/visibility")
        {
            Content = JsonContent.Create(new { isPublic = true })
        };
        request.Headers.Add("X-Requested-With", "BotManager");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);

        // A failed (404) action must not be audited.
        var missing = new HttpRequestMessage(HttpMethod.Put, "/api/bot/admin/999999/visibility")
        {
            Content = JsonContent.Create(new { isPublic = true })
        };
        missing.Headers.Add("X-Requested-With", "BotManager");
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(missing)).StatusCode);

        await using (var db = _factory.CreateDbContext())
        {
            var entry = await db.AdminAuditLogs.AsNoTracking()
                .Where(l => l.Action == "bot.visibility" && l.TargetId == ApiFactory.LegacyBotId.ToString())
                .OrderByDescending(l => l.Id)
                .FirstOrDefaultAsync();
            Assert.NotNull(entry);
            Assert.Equal("bot", entry!.TargetType);
            Assert.Equal(ApiFactory.AdminEmail, entry.ActorEmail);
            Assert.Equal("10.8.0.1", entry.IpAddress);
            Assert.Contains("\"isPublic\":true", entry.Details);
            Assert.False(await db.AdminAuditLogs.AnyAsync(l => l.TargetId == "999999"));
        }

        var json = await client.GetFromJsonAsync<JsonElement>("/api/systemlogs/admin-audit?take=5000");
        Assert.True(json.GetArrayLength() is > 0 and <= 500);
        var first = json.EnumerateArray().First(e => e.GetProperty("action").GetString() == "bot.visibility");
        Assert.EndsWith("Z", first.GetProperty("timestamp").GetString());

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _factory.CreateClientFrom("10.8.0.2").GetAsync("/api/systemlogs/admin-audit")).StatusCode);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client)
        => await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });

    private static async Task<HttpResponseMessage> PostWithCsrfAsync(HttpClient client, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(new { }) };
        request.Headers.Add("X-Requested-With", "BotManager");
        return await client.SendAsync(request);
    }

    private static string ExtractCookie(HttpResponseMessage response, string name)
    {
        var header = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith(name + "="));
        return header[(name.Length + 1)..header.IndexOf(';')];
    }
}
