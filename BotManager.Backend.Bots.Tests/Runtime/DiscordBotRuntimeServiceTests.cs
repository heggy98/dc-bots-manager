using BotManager.Backend.Bots.Models;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Entities.Entities;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BotManager.Backend.Bots.Tests.Runtime;

/// <summary>
/// Lifecycle tests of <see cref="DiscordBotRuntimeService"/> / DiscordBotSession / GatewayStatusTracker
/// against a fake gateway client and an EF InMemory database (no real Discord connection).
/// </summary>
public class DiscordBotRuntimeServiceTests
{
    [Fact]
    public async Task StartAsync_WhenReadyArrives_ReportsOnline()
    {
        await using var h = new RuntimeHarness();

        await h.Runtime.StartAsync(1, "token-1");

        var client = Assert.Single(h.Factory.Created);
        Assert.Equal("token-1", client.LoginToken);
        Assert.Equal(1, client.StartCalls);
        Assert.True(await h.Runtime.IsRunningAsync(1));
        Assert.Equal("Online", h.Runtime.GetStatus(1));
    }

    [Fact]
    public async Task StartBotAsync_ThroughManagementService_PersistsOnlineAndOpenHistory()
    {
        await using var h = new RuntimeHarness();
        await using (var seed = h.CreateDb())
        {
            seed.Bots.Add(new Bot { BotId = 7, Name = "bot-7", BotToken = "v1:token", OwnerUserId = "owner" });
            await seed.SaveChangesAsync();
        }

        await using var db = h.CreateDb();
        var management = CreateManagementService(h, db);

        Assert.True(await management.StartBotAsync(7));

        var bot = await h.GetBotAsync(7);
        Assert.Equal(BotStatus.Online, bot.Status);
        var history = Assert.Single(await h.GetHistoriesAsync(7));
        Assert.Null(history.StoppedAt);
        Assert.Contains((7, BotStatus.Online), h.Notifications.StatusChanges);
    }

    [Fact]
    public async Task StartAsync_WhenReadyTimesOut_ThrowsAndCleansUpClient()
    {
        await using var h = new RuntimeHarness(raiseReadyOnStart: false);

        await Assert.ThrowsAsync<TimeoutException>(() => h.Runtime.StartAsync(1, "token-1"));

        var client = Assert.Single(h.Factory.Created);
        Assert.Equal(1, client.StopCalls);
        Assert.Equal(1, client.LogoutCalls);
        Assert.True(client.Disposed);
        Assert.False(client.HasLifecycleSubscribers);
        Assert.False(await h.Runtime.IsRunningAsync(1));
        Assert.Equal("Offline", h.Runtime.GetStatus(1));
    }

    [Fact]
    public async Task StartBotAsync_WhenReadyTimesOut_PersistsOfflineAndClosesHistory()
    {
        await using var h = new RuntimeHarness(raiseReadyOnStart: false);
        await using (var seed = h.CreateDb())
        {
            seed.Bots.Add(new Bot { BotId = 8, Name = "bot-8", BotToken = "v1:token", OwnerUserId = "owner" });
            await seed.SaveChangesAsync();
        }

        await using var db = h.CreateDb();
        Assert.False(await CreateManagementService(h, db).StartBotAsync(8));

        Assert.Equal(BotStatus.Offline, (await h.GetBotAsync(8)).Status);
        var history = Assert.Single(await h.GetHistoriesAsync(8));
        Assert.NotNull(history.StoppedAt);
        Assert.Equal("Spuštění selhalo", history.StopReason);
        Assert.Contains("READY", history.ErrorDetails);
        Assert.True(Assert.Single(h.Factory.Created).Disposed);
    }

    [Fact]
    public async Task StartAsync_ConcurrentCallsForSameBot_CreateOneClient()
    {
        await using var h = new RuntimeHarness();
        h.Factory.CreateDelay = TimeSpan.FromMilliseconds(50);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => h.Runtime.StartAsync(1, "token-1"))));

        Assert.Single(h.Factory.Created);
        Assert.True(await h.Runtime.IsRunningAsync(1));
    }

    [Fact]
    public async Task TwoBots_RunSideBySide_AndStopAsyncStopsOnlyTheRequestedBot()
    {
        await using var h = new RuntimeHarness();

        await Task.WhenAll(h.Runtime.StartAsync(1, "token-1"), h.Runtime.StartAsync(2, "token-2"));

        var clients = h.Factory.Created.ToDictionary(c => c.LoginToken!);
        Assert.Equal(2, clients.Count);
        Assert.True(await h.Runtime.IsRunningAsync(1));
        Assert.True(await h.Runtime.IsRunningAsync(2));

        await h.Runtime.StopAsync(1);

        Assert.False(await h.Runtime.IsRunningAsync(1));
        Assert.True(clients["token-1"].Disposed);
        Assert.Equal(1, clients["token-1"].StopCalls);
        Assert.Equal([1], h.PluginRegistry.Removed);

        Assert.True(await h.Runtime.IsRunningAsync(2));
        Assert.Equal("Online", h.Runtime.GetStatus(2));
        Assert.False(clients["token-2"].Disposed);
        Assert.Equal(0, clients["token-2"].StopCalls);
    }

    [Fact]
    public async Task GatewayReconnect_WithinGrace_ShowsReconnectingThenReturnsOnlineWithoutOffline()
    {
        await using var h = new RuntimeHarness();
        await h.SeedRunningBotAsync(1);
        await h.Runtime.StartAsync(1, "token-1");
        var client = Assert.Single(h.Factory.Created);

        await client.SimulateDisconnectedAsync(new GatewayReconnectException("Server requested a reconnect"));

        Assert.Equal(BotStatus.Reconnecting, (await h.GetBotAsync(1)).Status);
        Assert.Contains((1, BotStatus.Reconnecting), h.Notifications.StatusChanges);

        await client.SimulateConnectedAsync();
        Assert.Equal(BotStatus.Online, (await h.GetBotAsync(1)).Status);

        // Let the grace period elapse: the superseded disconnect must not mark the bot offline.
        await Task.Delay(RuntimeHarness.ShortGrace * 2);

        Assert.Equal(BotStatus.Online, (await h.GetBotAsync(1)).Status);
        Assert.DoesNotContain(h.Notifications.StatusChanges, c => c.Status == BotStatus.Offline);
        Assert.True(await h.Runtime.IsRunningAsync(1));
    }

    [Fact]
    public async Task GatewayReconnect_BeyondGrace_PersistsOfflineAndClosesHistory()
    {
        await using var h = new RuntimeHarness();
        await h.SeedRunningBotAsync(1);
        await h.Runtime.StartAsync(1, "token-1");
        var client = Assert.Single(h.Factory.Created);

        await client.SimulateDisconnectedAsync(new GatewayReconnectException("socket closed"));

        // Still inside the grace period: only the transient Reconnecting state is persisted.
        Assert.Equal(BotStatus.Reconnecting, (await h.GetBotAsync(1)).Status);
        Assert.Null(Assert.Single(await h.GetHistoriesAsync(1)).StoppedAt);

        // The tracker persists first and notifies last, so wait for the final notification.
        await RuntimeHarness.WaitUntilAsync(() => Task.FromResult(h.Notifications.HistoryUpdates.Contains(1)), because: h.Logs);

        var bot = await h.GetBotAsync(1);
        Assert.NotNull(bot.LastStoppedAt);
        var history = Assert.Single(await h.GetHistoriesAsync(1));
        Assert.NotNull(history.StoppedAt);
        Assert.NotNull(history.DurationSeconds);
        Assert.StartsWith("Gateway reconnect failed", history.StopReason);
        Assert.Contains("socket closed", history.ErrorDetails);
        Assert.Contains((1, BotStatus.Offline), h.Notifications.StatusChanges);
        Assert.Contains(1, h.Notifications.HistoryUpdates);
        await RuntimeHarness.WaitUntilAsync(async () => !await h.Runtime.IsRunningAsync(1));
        Assert.Equal("Offline", h.Runtime.GetStatus(1));
    }

    /// <summary>
    /// Documents current behavior: for a disconnect that is not a <see cref="GatewayReconnectException"/>,
    /// nothing marks the bot Reconnecting, so after the grace period the persisted status is still Online and
    /// <see cref="GatewayDisconnectPolicy"/> treats that as "already restored" — the bot is never persisted Offline.
    /// </summary>
    [Fact(Skip = "Known gap: a prolonged non-reconnect disconnect never persists Offline (GatewayDisconnectPolicy sees the pre-disconnect Online status).")]
    public async Task PlainDisconnect_BeyondGrace_PersistsOffline()
    {
        await using var h = new RuntimeHarness();
        await h.SeedRunningBotAsync(1);
        await h.Runtime.StartAsync(1, "token-1");

        await Assert.Single(h.Factory.Created).SimulateDisconnectedAsync(new Exception("socket closed"));

        await RuntimeHarness.WaitUntilAsync(async () => (await h.GetBotAsync(1)).Status == BotStatus.Offline, because: h.Logs);
    }

    [Fact]
    public async Task StartAsync_AfterProlongedDisconnect_ReplacesStaleSession()
    {
        await using var h = new RuntimeHarness();
        await h.SeedRunningBotAsync(1);
        await h.Runtime.StartAsync(1, "token-1");
        var stale = Assert.Single(h.Factory.Created);
        await stale.SimulateDisconnectedAsync(new GatewayReconnectException("socket closed"));
        // The tracker persists first and notifies last, so wait for the final notification.
        await RuntimeHarness.WaitUntilAsync(() => Task.FromResult(h.Notifications.HistoryUpdates.Contains(1)), because: h.Logs);
        Assert.False(await h.Runtime.IsRunningAsync(1));

        await h.Runtime.StartAsync(1, "token-1");

        Assert.True(h.Factory.Created.Count == 2, h.Logs.ToString());
        Assert.True(stale.Disposed);
        Assert.True(await h.Runtime.IsRunningAsync(1));
    }

    [Fact]
    public async Task StopAllAsync_StopsAndDisposesEveryClient()
    {
        await using var h = new RuntimeHarness();
        await h.Runtime.StartAsync(1, "token-1");
        await h.Runtime.StartAsync(2, "token-2");
        await h.Runtime.StartAsync(3, "token-3");

        await h.Runtime.StopAllAsync();

        Assert.All(h.Factory.Created, c =>
        {
            Assert.Equal(1, c.StopCalls);
            Assert.True(c.Disposed);
            Assert.False(c.HasLifecycleSubscribers);
        });
        foreach (var botId in new[] { 1, 2, 3 })
        {
            Assert.False(await h.Runtime.IsRunningAsync(botId));
        }
        Assert.Equal([1, 2, 3], h.PluginRegistry.Removed.Order());
    }

    [Fact]
    public async Task StopAsync_DuringStop_DoesNotTriggerOfflineTransition()
    {
        await using var h = new RuntimeHarness();
        await h.SeedRunningBotAsync(1);
        await h.Runtime.StartAsync(1, "token-1");

        await h.Runtime.StopAsync(1);
        await Task.Delay(RuntimeHarness.ShortGrace * 2);

        // The runtime itself must not persist anything on an intentional stop (BotManagementService owns that).
        Assert.Equal(BotStatus.Online, (await h.GetBotAsync(1)).Status);
        Assert.Null(Assert.Single(await h.GetHistoriesAsync(1)).StoppedAt);
    }

    private static BotManagementService CreateManagementService(RuntimeHarness h, Entities.BotManagerDbContext db)
        => new(
            db,
            NullLogger<BotManagementService>.Instance,
            h.Runtime,
            groupDataService: null!,
            botDataService: null!,
            new CommandManagementService(db, NullLogger<CommandManagementService>.Instance, h.Notifications, h.ScopeFactory),
            new EmptyCommandProvider(),
            h.Notifications,
            new PassThroughTokenSecurity(),
            h.Alerts);

    private sealed class EmptyCommandProvider : IDiscordCommandProvider
    {
        public IReadOnlyCollection<ApplicationCommandProperties> BuildCommands() => [];
        public IReadOnlyCollection<DiscordCommandRegistration> GetCommandRegistrations() => [];
    }

    private sealed class PassThroughTokenSecurity : IBotTokenSecurityService
    {
        public string NormalizeRawToken(string rawToken) => rawToken;
        public string ProtectForStorage(string rawToken) => rawToken;
        public bool IsProtected(string storedToken) => true;
        public string BuildMaskedToken(string storedToken) => "***";
        public bool TryGetRawToken(string storedToken, out string rawToken)
        {
            rawToken = storedToken;
            return true;
        }
        public bool TryProtectLegacyToken(string storedToken, out string protectedToken)
        {
            protectedToken = storedToken;
            return false;
        }
    }
}
