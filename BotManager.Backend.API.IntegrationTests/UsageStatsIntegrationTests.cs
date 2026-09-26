using BotManager.Backend.Entities.Entities;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BotManager.Backend.API.IntegrationTests;

[Collection(ApiCollection.Name)]
public class UsageStatsIntegrationTests
{
    private readonly ApiFactory _factory;

    public UsageStatsIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Stats_AggregateInSql_ForDashboardAndSingleBot()
    {
        var botId = await SeedBotWithUsageAsync();
        var client = _factory.CreateClientFrom("10.20.0.1");
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var dashboard = await client.GetFromJsonAsync<JsonElement>("/api/bot/admin/stats?days=7");
        Assert.Equal(7, dashboard.GetProperty("days").GetInt32());
        Assert.Equal(7, dashboard.GetProperty("daily").GetArrayLength());
        Assert.True(dashboard.GetProperty("totals").GetProperty("commands").GetInt32() >= 3);
        Assert.Contains(dashboard.GetProperty("uptime").EnumerateArray(), u => u.GetProperty("botId").GetInt32() == botId);

        var single = await client.GetFromJsonAsync<JsonElement>($"/api/bot/admin/{botId}/stats?days=500");
        Assert.Equal(90, single.GetProperty("days").GetInt32());
        Assert.Equal(90, single.GetProperty("daily").GetArrayLength());
        var totals = single.GetProperty("totals");
        Assert.Equal(3, totals.GetProperty("commands").GetInt32());
        Assert.Equal(1, totals.GetProperty("errors").GetInt32());
        Assert.Equal(1, totals.GetProperty("joins").GetInt32());
        var today = single.GetProperty("daily").EnumerateArray().Last();
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), today.GetProperty("date").GetString());
        Assert.Equal(2, today.GetProperty("commands").GetInt32());
        Assert.Equal("/board add-team", single.GetProperty("topCommands")[0].GetProperty("command").GetString());
        var uptime = single.GetProperty("uptime")[0].GetProperty("uptimePercent").GetDouble();
        Assert.InRange(uptime, 0.01, 100);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/bot/admin/987654/stats")).StatusCode);
    }

    private async Task<int> SeedBotWithUsageAsync()
    {
        await using var db = _factory.CreateDbContext();
        var bot = new Bot { Name = "stats-bot", BotToken = "stats-token", OwnerUserId = ApiFactory.AdminEmail };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var command = new BotCommand { BotId = bot.BotId, CommandName = "board", SubCommandName = "add-team" };
        var reaction = new BotCommand { BotId = bot.BotId, CommandName = "reaction-assign" };
        db.BotCommands.AddRange(command, reaction);
        await db.SaveChangesAsync();

        // Keep "today" rows safely inside the current UTC day.
        var now = DateTime.UtcNow;
        var today = now.AddMinutes(-5) >= now.Date ? now.AddMinutes(-5) : now.Date;
        db.CommandUsageLogs.AddRange(
            new CommandUsageLog { CommandId = command.CommandId, UserId = 1, ExecutedAt = today, IsSuccess = true },
            new CommandUsageLog { CommandId = command.CommandId, UserId = 1, ExecutedAt = today, IsSuccess = false, ErrorMessage = "x" },
            new CommandUsageLog { CommandId = reaction.CommandId, UserId = 2, ExecutedAt = now.AddDays(-2), IsSuccess = true },
            new CommandUsageLog { CommandId = command.CommandId, UserId = 1, ExecutedAt = now.AddDays(-120), IsSuccess = true });
        db.BotRunHistories.AddRange(
            new BotRunHistory { BotId = bot.BotId, StartedAt = now.AddDays(-1), StoppedAt = now.AddHours(-12) },
            new BotRunHistory { BotId = bot.BotId, StartedAt = now.AddHours(-1), StoppedAt = null });
        await db.SaveChangesAsync();
        return bot.BotId;
    }
}
