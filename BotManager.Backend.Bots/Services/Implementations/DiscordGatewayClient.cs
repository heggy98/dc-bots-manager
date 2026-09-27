using Discord;
using Discord.WebSocket;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Thin seam over the lifecycle subset of <see cref="DiscordSocketClient"/> used by
    /// <see cref="DiscordBotSession"/> (login/start/stop, connection state and gateway events),
    /// so the session lifecycle can be tested without a real Discord connection.
    /// </summary>
    internal interface IDiscordGatewayClient : IDisposable
    {
        /// <summary>The underlying socket client for REST/guild/interaction work; null for test fakes.</summary>
        DiscordSocketClient? SocketClient { get; }

        ConnectionState ConnectionState { get; }

        int Latency { get; }

        event Func<LogMessage, Task> Log;
        event Func<Task> Ready;
        event Func<Task> Connected;
        event Func<Exception, Task> Disconnected;
        event Func<int, int, Task> LatencyUpdated;

        Task LoginAsync(TokenType tokenType, string token);
        Task StartAsync();
        Task StopAsync();
        Task LogoutAsync();
    }

    /// <summary>
    /// Creates gateway clients for bot sessions.
    /// </summary>
    internal interface IDiscordGatewayClientFactory
    {
        IDiscordGatewayClient Create(DiscordSocketConfig config);
    }

    /// <summary>
    /// Timing knobs of the Discord runtime; defaults match production behavior.
    /// </summary>
    internal sealed record DiscordRuntimeOptions
    {
        public static readonly DiscordRuntimeOptions Default = new();

        /// <summary>How long a start waits for the gateway READY event.</summary>
        public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>How long an unexpected disconnect may last before the bot is persisted as Offline.</summary>
        public TimeSpan DisconnectGrace { get; init; } = TimeSpan.FromSeconds(GatewayStatusTracker.DisconnectGraceSeconds);
    }

    /// <summary>
    /// Production factory: one real <see cref="DiscordSocketClient"/> per session start.
    /// </summary>
    internal sealed class DiscordSocketGatewayClientFactory : IDiscordGatewayClientFactory
    {
        public static readonly DiscordSocketGatewayClientFactory Instance = new();

        public IDiscordGatewayClient Create(DiscordSocketConfig config)
            => new DiscordSocketGatewayClient(new DiscordSocketClient(config));
    }

    /// <summary>
    /// Pass-through adapter around <see cref="DiscordSocketClient"/>.
    /// </summary>
    internal sealed class DiscordSocketGatewayClient : IDiscordGatewayClient
    {
        private readonly DiscordSocketClient _client;

        public DiscordSocketGatewayClient(DiscordSocketClient client)
        {
            _client = client;
        }

        public DiscordSocketClient? SocketClient => _client;

        public ConnectionState ConnectionState => _client.ConnectionState;

        public int Latency => _client.Latency;

        public event Func<LogMessage, Task> Log { add => _client.Log += value; remove => _client.Log -= value; }
        public event Func<Task> Ready { add => _client.Ready += value; remove => _client.Ready -= value; }
        public event Func<Task> Connected { add => _client.Connected += value; remove => _client.Connected -= value; }
        public event Func<Exception, Task> Disconnected { add => _client.Disconnected += value; remove => _client.Disconnected -= value; }
        public event Func<int, int, Task> LatencyUpdated { add => _client.LatencyUpdated += value; remove => _client.LatencyUpdated -= value; }

        public Task LoginAsync(TokenType tokenType, string token) => _client.LoginAsync(tokenType, token);

        public Task StartAsync() => _client.StartAsync();

        public Task StopAsync() => _client.StopAsync();

        public Task LogoutAsync() => _client.LogoutAsync();

        public void Dispose() => _client.Dispose();
    }
}
