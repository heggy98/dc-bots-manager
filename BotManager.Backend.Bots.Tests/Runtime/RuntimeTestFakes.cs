using System.Collections.Concurrent;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Shared.Models;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BotManager.Backend.Bots.Tests.Runtime;

/// <summary>
/// Scriptable stand-in for the Discord gateway: records lifecycle calls and lets tests raise gateway events.
/// </summary>
internal sealed class FakeGatewayClient : IDiscordGatewayClient
{
    private int _stopCalls;
    private int _disposeCalls;

    public FakeGatewayClient(bool raiseReadyOnStart)
    {
        RaiseReadyOnStart = raiseReadyOnStart;
    }

    public bool RaiseReadyOnStart { get; }
    public DiscordSocketClient? SocketClient => null;
    public ConnectionState ConnectionState { get; set; } = ConnectionState.Disconnected;
    public int Latency => 42;
    public string? LoginToken { get; private set; }
    public int StartCalls { get; private set; }
    public int StopCalls => Volatile.Read(ref _stopCalls);
    public int LogoutCalls { get; private set; }
    public bool Disposed => Volatile.Read(ref _disposeCalls) > 0;

    public event Func<LogMessage, Task>? Log;
    public event Func<Task>? Ready;
    public event Func<Task>? Connected;
    public event Func<Exception, Task>? Disconnected;
    public event Func<int, int, Task>? LatencyUpdated;

    public bool HasLifecycleSubscribers => Ready != null || Connected != null || Disconnected != null || Log != null || LatencyUpdated != null;

    public Task LoginAsync(TokenType tokenType, string token)
    {
        LoginToken = token;
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        StartCalls++;
        ConnectionState = ConnectionState.Connecting;
        if (RaiseReadyOnStart)
        {
            // Like Discord.Net: the gateway connects and then dispatches READY asynchronously.
            _ = Task.Run(async () =>
            {
                await Task.Delay(20);
                await SimulateConnectedAsync();
                if (Ready != null) await Ready.Invoke();
            });
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        Interlocked.Increment(ref _stopCalls);
        ConnectionState = ConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    public Task LogoutAsync()
    {
        LogoutCalls++;
        return Task.CompletedTask;
    }

    public void Dispose() => Interlocked.Increment(ref _disposeCalls);

    public async Task SimulateConnectedAsync()
    {
        ConnectionState = ConnectionState.Connected;
        if (Connected != null) await Connected.Invoke();
    }

    public async Task SimulateDisconnectedAsync(Exception ex)
    {
        ConnectionState = ConnectionState.Connecting;
        if (Disconnected != null) await Disconnected.Invoke(ex);
    }
}

internal sealed class FakeGatewayClientFactory : IDiscordGatewayClientFactory
{
    private readonly bool _raiseReadyOnStart;

    public FakeGatewayClientFactory(bool raiseReadyOnStart = true)
    {
        _raiseReadyOnStart = raiseReadyOnStart;
    }

    public ConcurrentQueue<FakeGatewayClient> Created { get; } = new();

    /// <summary>Optional delay inside Create, to widen race windows in concurrency tests.</summary>
    public TimeSpan CreateDelay { get; set; } = TimeSpan.Zero;

    public IDiscordGatewayClient Create(DiscordSocketConfig config)
    {
        if (CreateDelay > TimeSpan.Zero) Thread.Sleep(CreateDelay);
        var client = new FakeGatewayClient(_raiseReadyOnStart);
        Created.Enqueue(client);
        return client;
    }
}

internal sealed class RecordingNotificationService : IBotNotificationService
{
    public ConcurrentQueue<(int BotId, BotStatus Status)> StatusChanges { get; } = new();
    public ConcurrentQueue<int> HistoryUpdates { get; } = new();

    public Task NotifyBotStatusChangedAsync(int botId, BotStatus status)
    {
        StatusChanges.Enqueue((botId, status));
        return Task.CompletedTask;
    }

    public Task NotifyNewLogAsync(int botId, string level, string message, DateTime timestamp) => Task.CompletedTask;
    public Task NotifyStatsUpdatedAsync(int botId, int requests24h, int errors24h) => Task.CompletedTask;

    public Task NotifyHistoryUpdatedAsync(int botId)
    {
        HistoryUpdates.Enqueue(botId);
        return Task.CompletedTask;
    }
}

internal sealed class FakePluginRegistry : IPluginRegistry
{
    public ConcurrentQueue<int> Removed { get; } = new();
    public void RegisterPlugin(string pluginId, Type pluginType) { }
    public IDiscordBotPlugin GetOrCreatePlugin(int botId, string pluginId) => throw new NotSupportedException();
    public void RemovePlugin(int botId) => Removed.Enqueue(botId);
    public IDiscordBotPlugin? GetPlugin(int botId) => null;
    public IEnumerable<string> GetRegisteredPluginIds() => [];
    public bool IsPluginRegistered(string pluginId) => false;
}

internal sealed class NullBoardMessageLocator : IBoardMessageLocator
{
    public Task<IUserMessage?> FindBoardMessageAsync(SocketTextChannel channel, string marker, int historyLimit = 100)
        => Task.FromResult<IUserMessage?>(null);
}

/// <summary>
/// Wires the runtime with fakes and a shared EF InMemory database, using short timeouts.
/// </summary>
internal sealed class RuntimeHarness : IAsyncDisposable
{
    /// <summary>Generous for success paths (READY is raised after EF work in the Connected handler).</summary>
    public static readonly TimeSpan DefaultReadyTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ShortReadyTimeout = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan ShortGrace = TimeSpan.FromMilliseconds(400);

    private readonly ServiceProvider _services;

    public RuntimeHarness(bool raiseReadyOnStart = true, TimeSpan? readyTimeout = null, TimeSpan? grace = null)
    {
        var dbName = Guid.NewGuid().ToString();
        var dbRoot = new InMemoryDatabaseRoot();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<BotManagerDbContext>(o => o.UseInMemoryDatabase(dbName, dbRoot));
        services.AddMetrics();
        _services = services.BuildServiceProvider();

        Factory = new FakeGatewayClientFactory(raiseReadyOnStart);
        Runtime = new DiscordBotRuntimeService(
            NullLogger<DiscordBotRuntimeService>.Instance,
            LoggerFactory.Create(b => b.AddProvider(Logs).SetMinimumLevel(LogLevel.Debug)),
            _services.GetRequiredService<IServiceScopeFactory>(),
            PluginRegistry,
            new NullBoardMessageLocator(),
            Notifications,
            Alerts,
            new BotManagerMetrics(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            Factory,
            new DiscordRuntimeOptions
            {
                ReadyTimeout = readyTimeout ?? (raiseReadyOnStart ? DefaultReadyTimeout : ShortReadyTimeout),
                DisconnectGrace = grace ?? ShortGrace
            });
    }

    public RecordingAlertService Alerts { get; } = new();
    public CapturingLoggerProvider Logs { get; } = new();
    public FakeGatewayClientFactory Factory { get; }
    public RecordingNotificationService Notifications { get; } = new();
    public FakePluginRegistry PluginRegistry { get; } = new();
    public DiscordBotRuntimeService Runtime { get; }

    public IServiceScopeFactory ScopeFactory => _services.GetRequiredService<IServiceScopeFactory>();

    public BotManagerDbContext CreateDb() => _services.CreateScope().ServiceProvider.GetRequiredService<BotManagerDbContext>();

    /// <summary>Seeds a bot in the state BotManagementService leaves it in right before calling the runtime.</summary>
    public async Task SeedRunningBotAsync(int botId, BotStatus status = BotStatus.Online)
    {
        await using var db = CreateDb();
        db.Bots.Add(new Bot { BotId = botId, Name = $"bot-{botId}", BotToken = "v1:token", OwnerUserId = "owner", Status = status, LastStartedAt = DateTime.UtcNow });
        db.BotRunHistories.Add(new BotRunHistory { BotId = botId, StartedAt = DateTime.UtcNow.AddMinutes(-5) });
        await db.SaveChangesAsync();
    }

    public async Task<Bot> GetBotAsync(int botId)
    {
        await using var db = CreateDb();
        return await db.Bots.AsNoTracking().SingleAsync(b => b.BotId == botId);
    }

    public async Task<List<BotRunHistory>> GetHistoriesAsync(int botId)
    {
        await using var db = CreateDb();
        return await db.BotRunHistories.AsNoTracking().Where(h => h.BotId == botId).OrderBy(h => h.Id).ToListAsync();
    }

    /// <summary>Polls a condition until it holds (for work the runtime completes in the background).</summary>
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null, object? because = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Condition was not met in time.{Environment.NewLine}{because}");
            await Task.Delay(20);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Runtime.DisposeAsync();
        await _services.DisposeAsync();
    }
}

/// <summary>Collects log lines so failing tests can show what the runtime did.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Lines);

    public void Dispose() { }

    public override string ToString() => string.Join(Environment.NewLine, Lines);

    private sealed class CapturingLogger(ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => lines.Enqueue($"{logLevel}: {formatter(state, exception)}{(exception == null ? "" : " | " + exception.GetType().Name + ": " + exception.Message)}");
    }
}

/// <summary>
/// Alert service fake that records alerts instead of sending them.
/// </summary>
internal sealed class RecordingAlertService : IBotAlertService
{
    public bool IsEnabled => true;

    public System.Collections.Concurrent.ConcurrentQueue<BotAlert> Alerts { get; } = new();

    public Task NotifyAsync(BotAlert alert, CancellationToken cancellationToken = default)
    {
        Alerts.Enqueue(alert);
        return Task.CompletedTask;
    }
}
