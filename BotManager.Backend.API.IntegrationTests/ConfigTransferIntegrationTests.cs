using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace BotManager.Backend.API.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ConfigTransferIntegrationTests
{
    private const string SourceToken = "source-raw-token-must-never-leak";
    private readonly ApiFactory _factory;

    public ConfigTransferIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Export_NeverContainsTheToken()
    {
        var botId = await SeedBotAsync("export-bot");
        var client = await LoggedInClientAsync("10.20.0.2");

        var response = await client.GetAsync($"/api/bot/admin/{botId}/export");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.ToString() ?? response.Headers.ToString());
        Assert.DoesNotContain(SourceToken, json);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("roleId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("boardMessageId", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"schemaVersion\":1", json);
    }

    [Fact]
    public async Task ExportImport_RoundTripsIntoAnotherBot()
    {
        var sourceId = await SeedBotAsync("roundtrip-source");
        var targetId = await SeedBotAsync("roundtrip-target", boards: false);
        var client = await LoggedInClientAsync("10.20.0.3");

        var exported = await client.GetStringAsync($"/api/bot/admin/{sourceId}/export");

        var dryRun = await PostJsonWithCsrfAsync(client, $"/api/bot/admin/{targetId}/import?dryRun=true", exported);
        Assert.Equal(HttpStatusCode.OK, dryRun.StatusCode);
        var drySummary = await dryRun.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(drySummary.GetProperty("dryRun").GetBoolean());
        await using (var db = _factory.CreateDbContext())
        {
            Assert.Equal(0, await db.BoardConfigurations.CountAsync(b => b.BotId == targetId));
        }

        var import = await PostJsonWithCsrfAsync(client, $"/api/bot/admin/{targetId}/import", exported);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        var summary = await import.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, summary.GetProperty("boardsImported").GetInt32());
        Assert.Equal(3, summary.GetProperty("teamsImported").GetInt32());

        var reExported = await client.GetStringAsync($"/api/bot/admin/{targetId}/export");
        Assert.Equal(StripVolatile(exported), StripVolatile(reExported));

        await using (var db = _factory.CreateDbContext())
        {
            var target = await db.Bots.AsNoTracking().SingleAsync(b => b.BotId == targetId);
            Assert.Equal("roundtrip-source", target.Name);
            Assert.Equal(SourceToken + "-roundtrip-target", target.BotToken);
            var config = await db.BotConfigurations.AsNoTracking().SingleAsync(c => c.BotId == targetId);
            var activeBoard = await db.BoardConfigurations.AsNoTracking().SingleAsync(b => b.BoardConfigurationId == config.ActiveBoardConfigurationId);
            Assert.Equal("Second board", activeBoard.BoardTitle);
        }

        // Import again (replace) is idempotent in shape.
        var again = await PostJsonWithCsrfAsync(client, $"/api/bot/admin/{targetId}/import?mode=replace", exported);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        await using (var db = _factory.CreateDbContext())
        {
            Assert.Equal(2, await db.BoardConfigurations.CountAsync(b => b.BotId == targetId));
        }
    }

    [Fact]
    public async Task Import_InvalidDocuments_AreRejected_WithoutChanges()
    {
        var botId = await SeedBotAsync("invalid-import-bot");
        var client = await LoggedInClientAsync("10.20.0.4");
        var exported = JsonNode.Parse(await client.GetStringAsync($"/api/bot/admin/{botId}/export"))!;

        var wrongVersion = exported.DeepClone();
        wrongVersion["schemaVersion"] = 7;
        var response = await PostJsonWithCsrfAsync(client, $"/api/bot/admin/{botId}/import", wrongVersion.ToJsonString());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(body.GetProperty("errors").EnumerateArray(), e => e.GetString()!.StartsWith("schemaVersion"));

        var duplicateEmoji = exported.DeepClone();
        duplicateEmoji["bot"]!["name"] = "should-not-be-applied";
        duplicateEmoji["boards"]![0]!["teams"]![1]!["emoji"] = duplicateEmoji["boards"]![0]!["teams"]![0]!["emoji"]!.GetValue<string>();
        response = await PostJsonWithCsrfAsync(client, $"/api/bot/admin/{botId}/import", duplicateEmoji.ToJsonString());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        response = await PostJsonWithCsrfAsync(client, $"/api/bot/admin/{botId}/import", "{ not json");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        response = await PostJsonWithCsrfAsync(client, $"/api/bot/admin/{botId}/import?mode=merge", exported.ToJsonString());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Cookie-authenticated import without the CSRF header is refused by the middleware.
        var noCsrf = await client.PostAsync($"/api/bot/admin/{botId}/import",
            new StringContent(exported.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);

        await using var db = _factory.CreateDbContext();
        Assert.Equal("invalid-import-bot", (await db.Bots.AsNoTracking().SingleAsync(b => b.BotId == botId)).Name);
        Assert.Equal(2, await db.BoardConfigurations.CountAsync(b => b.BotId == botId));
    }

    /// <summary>
    /// Seeds a bot owned by the admin, optionally with two boards (three teams), plus customized commands.
    /// </summary>
    private async Task<int> SeedBotAsync(string name, bool boards = true)
    {
        await using var db = _factory.CreateDbContext();
        var bot = new Bot { Name = name, BotToken = SourceToken + "-" + name, OwnerUserId = ApiFactory.AdminEmail };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        if (boards)
        {
            var first = new BoardConfiguration
            {
                BotId = bot.BotId,
                BoardTitle = "First board",
                GuildId = 123456789012345678,
                BoardChannelId = 223456789012345678,
                BoardMessageId = 323456789012345678,
                Teams =
                [
                    new Team { Name = "Alpha", LeaderName = "Al", CommanderContact = "@al", Emoji = "✅", RoleId = 423456789012345678 },
                    new Team { Name = "Bravo", LeaderName = "Bo", CommanderContact = "@bo", Emoji = "🎯" }
                ]
            };
            var second = new BoardConfiguration
            {
                BotId = bot.BotId,
                BoardTitle = "Second board",
                Teams = [new Team { Name = "Charlie", Emoji = "✅" }]
            };
            db.BoardConfigurations.AddRange(first, second);
            await db.SaveChangesAsync();
            db.BotConfigurations.Add(new BotConfiguration { BotId = bot.BotId, ActiveBoardConfigurationId = second.BoardConfigurationId });
        }

        var command = new BotCommand
        {
            BotId = bot.BotId,
            CommandName = "board",
            SubCommandName = "add-team",
            Description = "Customized",
            MinimumPermissionLevel = 1,
            IsEnabled = true,
            SuccessMessage = "Done!"
        };
        var reaction = new BotCommand { BotId = bot.BotId, CommandName = "reaction-assign" };
        db.BotCommands.AddRange(command, reaction);
        await db.SaveChangesAsync();

        return bot.BotId;
    }

    private async Task<HttpClient> LoggedInClientAsync(string ip)
    {
        var client = _factory.CreateClientFrom(ip);
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private static async Task<HttpResponseMessage> PostJsonWithCsrfAsync(HttpClient client, string url, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Requested-With", "BotManager");
        return await client.SendAsync(request);
    }

    private static string StripVolatile(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node.Remove("exportedAt");
        return node.ToJsonString();
    }
}
