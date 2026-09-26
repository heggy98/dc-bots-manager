using BotManager.Backend.API.Services;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BotManager.Backend.API.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TwoFactorIntegrationTests
{
    private readonly ApiFactory _factory;

    public TwoFactorIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TwoFactor_EnforcedAtLogin_AndAudited()
    {
        var admin = _factory.CreateClientFrom("10.9.0.1");
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(admin)).StatusCode);

        try
        {
            var status = await admin.GetFromJsonAsync<JsonElement>("/api/auth/2fa/status");
            Assert.False(status.GetProperty("enabled").GetBoolean());

            var setupResponse = await PostAsync(admin, "/api/auth/2fa/setup", new { });
            Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
            var setup = await setupResponse.Content.ReadFromJsonAsync<JsonElement>();
            var secret = setup.GetProperty("secret").GetString()!;
            Assert.StartsWith("otpauth://totp/", setup.GetProperty("otpAuthUri").GetString());
            var key = Totp.Base32Decode(secret);

            // Not enforced before confirmation.
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_factory.CreateClientFrom("10.9.0.2"))).StatusCode);

            // The generic config API neither lists nor accepts the 2FA keys.
            var configs = await admin.GetStringAsync("/api/systemconfig");
            Assert.DoesNotContain("Auth.Totp", configs);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await SendAsync(admin, HttpMethod.Put, "/api/systemconfig", new { key = "Auth.TotpEnabled", value = "false" })).StatusCode);

            var step = Totp.GetTimeStep(DateTimeOffset.UtcNow);
            var enableResponse = await PostAsync(admin, "/api/auth/2fa/enable", new { code = Totp.ComputeCode(key, step) });
            Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);
            var recoveryCodes = (await enableResponse.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToList();
            Assert.Equal(8, recoveryCodes.Count);

            // Password only: 401 with twoFactorRequired and no session cookies.
            var noCode = await LoginAsync(_factory.CreateClientFrom("10.9.0.3"));
            Assert.Equal(HttpStatusCode.Unauthorized, noCode.StatusCode);
            Assert.True((await noCode.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("twoFactorRequired").GetBoolean());
            Assert.False(noCode.Headers.TryGetValues("Set-Cookie", out var noCodeCookies)
                         && noCodeCookies.Any(c => c.StartsWith("bm_access=")));

            // Wrong code: 401 and counted as a failed attempt.
            var wrongCode = await LoginAsync(_factory.CreateClientFrom("10.9.0.4"), totpCode: Totp.ComputeCode(key, step + 10));
            Assert.Equal(HttpStatusCode.Unauthorized, wrongCode.StatusCode);
            var wrongBody = await wrongCode.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(wrongBody.GetProperty("invalidCode").GetBoolean());
            var attempts = await _factory.CreateClientFrom("10.9.0.4").GetFromJsonAsync<JsonElement>("/api/auth/attempt-status");
            Assert.Equal(1, attempts.GetProperty("attempts").GetInt32());

            // Replaying the code used for enabling is rejected.
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await LoginAsync(_factory.CreateClientFrom("10.9.0.5"), totpCode: Totp.ComputeCode(key, step))).StatusCode);

            // Next time step (within the ±1 window) works once.
            var nextCode = Totp.ComputeCode(key, step + 1);
            var ok = _factory.CreateClientFrom("10.9.0.6");
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(ok, totpCode: nextCode)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ok.GetAsync("/api/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await LoginAsync(_factory.CreateClientFrom("10.9.0.7"), totpCode: nextCode)).StatusCode);

            // A recovery code works exactly once.
            Assert.Equal(HttpStatusCode.OK,
                (await LoginAsync(_factory.CreateClientFrom("10.9.0.8"), recoveryCode: recoveryCodes[0])).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await LoginAsync(_factory.CreateClientFrom("10.9.0.9"), recoveryCode: recoveryCodes[0])).StatusCode);

            status = await admin.GetFromJsonAsync<JsonElement>("/api/auth/2fa/status");
            Assert.True(status.GetProperty("enabled").GetBoolean());
            Assert.Equal(7, status.GetProperty("recoveryCodesRemaining").GetInt32());

            // Disable requires the password as well.
            Assert.Equal(HttpStatusCode.BadRequest,
                (await PostAsync(admin, "/api/auth/2fa/disable", new { recoveryCode = recoveryCodes[1], password = "wrong" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await PostAsync(admin, "/api/auth/2fa/disable", new { recoveryCode = recoveryCodes[1], password = ApiFactory.AdminPassword })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_factory.CreateClientFrom("10.9.0.10"))).StatusCode);

            await using var db = _factory.CreateDbContext();
            var actions = await db.AdminAuditLogs.AsNoTracking()
                .Where(l => l.Action.StartsWith("auth.2fa."))
                .Select(l => l.Action + "|" + l.ActorEmail + "|" + (l.Details ?? ""))
                .ToListAsync();
            Assert.Contains(actions, a => a.StartsWith("auth.2fa.enable|" + ApiFactory.AdminEmail));
            Assert.Contains(actions, a => a.StartsWith("auth.2fa.disable|" + ApiFactory.AdminEmail));
            Assert.DoesNotContain(actions, a => a.Contains(secret) || recoveryCodes.Any(a.Contains));
            Assert.True(await db.LoginAuditLogs.AnyAsync(l => l.IpAddress == "10.9.0.4" && l.FailReason == "Neplatný 2FA kód"));
        }
        finally
        {
            // Never leave 2FA enabled for the other tests sharing this database.
            await using var db = _factory.CreateDbContext();
            await db.SystemConfigs.Where(c => c.Key.StartsWith(TwoFactorService.KeyPrefix)).ExecuteDeleteAsync();
        }
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string? totpCode = null, string? recoveryCode = null)
        => client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword, totpCode, recoveryCode });

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body)
        => SendAsync(client, HttpMethod.Post, url, body);

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object body)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Requested-With", "BotManager");
        return client.SendAsync(request);
    }
}
