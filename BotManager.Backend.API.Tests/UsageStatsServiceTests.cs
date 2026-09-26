using BotManager.Backend.API.Services;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BotManager.Backend.API.Tests;

public class UsageStatsServiceTests
{
    private static readonly DateTime WindowStart = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowEnd = new(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, 30)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(7, 7)]
    [InlineData(90, 90)]
    [InlineData(500, 90)]
    public void ClampDays_ClampsToSupportedRange(int? requested, int expected)
    {
        Assert.Equal(expected, UsageStatsService.ClampDays(requested));
    }

    [Fact]
    public void Uptime_IntervalInsideWindow_CountsFully()
    {
        var seconds = UptimeCalculator.ComputeUptimeSeconds(
            [new RunInterval(WindowStart.AddDays(1), WindowStart.AddDays(2))], WindowStart, WindowEnd);

        Assert.Equal((long)TimeSpan.FromDays(1).TotalSeconds, seconds);
    }

    [Fact]
    public void Uptime_IntervalsAreClippedToWindow()
    {
        var seconds = UptimeCalculator.ComputeUptimeSeconds(
            [
                new RunInterval(WindowStart.AddDays(-3), WindowStart.AddHours(2)), // starts before window
                new RunInterval(WindowEnd.AddHours(-1), WindowEnd.AddDays(4))      // ends after window
            ],
            WindowStart,
            WindowEnd);

        Assert.Equal((long)TimeSpan.FromHours(3).TotalSeconds, seconds);
    }

    [Fact]
    public void Uptime_OpenIntervalCountsUntilWindowEnd()
    {
        var seconds = UptimeCalculator.ComputeUptimeSeconds(
            [new RunInterval(WindowEnd.AddHours(-6), null)], WindowStart, WindowEnd);

        Assert.Equal((long)TimeSpan.FromHours(6).TotalSeconds, seconds);
    }

    [Fact]
    public void Uptime_OverlappingIntervalsAreNotDoubleCounted()
    {
        var seconds = UptimeCalculator.ComputeUptimeSeconds(
            [
                new RunInterval(WindowStart.AddHours(1), WindowStart.AddHours(5)),
                new RunInterval(WindowStart.AddHours(3), WindowStart.AddHours(8)),
                new RunInterval(WindowStart.AddHours(4), WindowStart.AddHours(6)),
                new RunInterval(WindowStart.AddHours(10), WindowStart.AddHours(11))
            ],
            WindowStart,
            WindowEnd);

        Assert.Equal((long)TimeSpan.FromHours(8).TotalSeconds, seconds);
    }

    [Fact]
    public void Uptime_IntervalsOutsideWindowOrInvertedAreIgnored()
    {
        var seconds = UptimeCalculator.ComputeUptimeSeconds(
            [
                new RunInterval(WindowStart.AddDays(-5), WindowStart.AddDays(-4)),
                new RunInterval(WindowEnd.AddDays(1), null),
                new RunInterval(WindowStart.AddHours(5), WindowStart.AddHours(4))
            ],
            WindowStart,
            WindowEnd);

        Assert.Equal(0, seconds);
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(50, 100, 50)]
    [InlineData(1, 3, 33.3)]
    [InlineData(200, 100, 100)]
    [InlineData(10, 0, 0)]
    public void ToPercent_RoundsAndClamps(long uptime, long window, double expected)
    {
        Assert.Equal(expected, UptimeCalculator.ToPercent(uptime, window));
    }

    [Fact]
    public void FillDays_ZeroFillsMissingDaysOldestFirst()
    {
        var days = UsageStatsService.FillDays(WindowStart, 3,
            [new UsageStatsService.DailyAggregate(WindowStart.AddDays(1), 5, 2, 1, 0)]);

        Assert.Equal(["2026-09-01", "2026-09-02", "2026-09-03"], days.Select(d => d.Date));
        Assert.Equal([0, 5, 0], days.Select(d => d.Commands));
        Assert.Equal(2, days[1].Errors);
        Assert.Equal(1, days[1].Joins);
    }

    [Fact]
    public async Task GetBotStatsAsync_AggregatesPerDay_TopCommands_AndUptime()
    {
        var now = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        await using var db = CreateContext();
        db.Bots.Add(new Bot { BotId = 1, Name = "one", BotToken = "t", OwnerUserId = "owner" });
        db.Bots.Add(new Bot { BotId = 2, Name = "two", BotToken = "t", OwnerUserId = "someone-else" });
        db.BotCommands.AddRange(
            new BotCommand { CommandId = 10, BotId = 1, CommandName = "board", SubCommandName = "add-team" },
            new BotCommand { CommandId = 11, BotId = 1, CommandName = "reaction-assign" },
            new BotCommand { CommandId = 20, BotId = 2, CommandName = "board", SubCommandName = "add-team" });
        db.CommandUsageLogs.AddRange(
            Log(10, now.AddHours(-1)),
            Log(10, now.AddHours(-2), success: false),
            Log(11, now.AddDays(-1)),
            Log(10, now.AddDays(-30)), // outside a 7-day window
            Log(20, now.AddHours(-1))); // other bot
        db.BotRunHistories.AddRange(
            new BotRunHistory { BotId = 1, StartedAt = now.AddHours(-12), StoppedAt = now.AddHours(-6) },
            new BotRunHistory { BotId = 1, StartedAt = now.AddHours(-3), StoppedAt = null });
        await db.SaveChangesAsync();

        var sut = new UsageStatsService(db, () => now);
        var stats = await sut.GetBotStatsAsync(1, 7);

        Assert.NotNull(stats);
        Assert.Equal(7, stats!.Days);
        Assert.Equal(new DateTime(2026, 9, 4), stats.From);
        Assert.Equal(7, stats.Daily.Count);
        Assert.Equal(3, stats.Totals.Commands);
        Assert.Equal(1, stats.Totals.Errors);
        Assert.Equal(1, stats.Totals.Joins);
        Assert.Equal(2, stats.Daily.Single(d => d.Date == "2026-09-10").Commands);
        Assert.Equal(1, stats.Daily.Single(d => d.Date == "2026-09-09").Joins);

        var top = stats.TopCommands.First();
        Assert.Equal("/board add-team", top.Command);
        Assert.Equal(2, top.Count);
        Assert.Equal(1, top.Errors);
        Assert.Contains(stats.TopCommands, c => c.Command == "[reaction-assign]");

        var uptime = Assert.Single(stats.Uptime);
        Assert.Equal((long)TimeSpan.FromHours(9).TotalSeconds, uptime.UptimeSeconds);
        Assert.Equal((long)(now - stats.From).TotalSeconds, uptime.WindowSeconds);
    }

    [Fact]
    public async Task GetOwnerStatsAsync_OnlyIncludesOwnersBots()
    {
        var now = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        await using var db = CreateContext();
        db.Bots.Add(new Bot { BotId = 1, Name = "mine", BotToken = "t", OwnerUserId = "owner" });
        db.Bots.Add(new Bot { BotId = 2, Name = "theirs", BotToken = "t", OwnerUserId = "other" });
        db.BotCommands.AddRange(
            new BotCommand { CommandId = 10, BotId = 1, CommandName = "board", SubCommandName = "list" },
            new BotCommand { CommandId = 20, BotId = 2, CommandName = "board", SubCommandName = "list" });
        db.CommandUsageLogs.AddRange(Log(10, now.AddHours(-1)), Log(20, now.AddHours(-1)), Log(20, now.AddHours(-2)));
        await db.SaveChangesAsync();

        var stats = await new UsageStatsService(db, () => now).GetOwnerStatsAsync("owner", 30);

        Assert.Equal(1, stats.Totals.Commands);
        Assert.Equal("mine", Assert.Single(stats.Uptime).Name);
        Assert.Equal(30, stats.Daily.Count);
    }

    [Fact]
    public async Task GetBotStatsAsync_UnknownBot_ReturnsNull()
    {
        await using var db = CreateContext();
        Assert.Null(await new UsageStatsService(db).GetBotStatsAsync(999, 30));
    }

    private static CommandUsageLog Log(int commandId, DateTime at, bool success = true)
        => new() { CommandId = commandId, UserId = 1, ExecutedAt = at, IsSuccess = success };

    private static BotManagerDbContext CreateContext()
        => new(new DbContextOptionsBuilder<BotManagerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
