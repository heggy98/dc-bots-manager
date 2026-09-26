using BotManager.Backend.Bots.Models;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Hosts Discord gateway runtimes for multiple bots. Each bot gets its own
    /// <see cref="DiscordBotSession"/>; start/stop of a bot is serialized per bot id.
    /// </summary>
    public class DiscordBotRuntimeService : IDiscordBotService, IAsyncDisposable
    {
        private readonly ConcurrentDictionary<int, DiscordBotSession> _sessions = new();
        private readonly ConcurrentDictionary<int, SemaphoreSlim> _lifecycleLocks = new();
        private readonly ILogger<DiscordBotRuntimeService> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IPluginRegistry _pluginRegistry;
        private readonly IBoardMessageLocator _boardMessageLocator;
        private readonly IBotNotificationService _notificationService;
        private readonly IDiscordGatewayClientFactory _clientFactory;
        private readonly DiscordRuntimeOptions _options;

        /// <summary>
        /// Creates a new Discord runtime service.
        /// </summary>
        public DiscordBotRuntimeService(
            ILogger<DiscordBotRuntimeService> logger,
            ILoggerFactory loggerFactory,
            IServiceScopeFactory scopeFactory,
            IPluginRegistry pluginRegistry,
            IBoardMessageLocator boardMessageLocator,
            IBotNotificationService notificationService)
            : this(logger, loggerFactory, scopeFactory, pluginRegistry, boardMessageLocator, notificationService,
                DiscordSocketGatewayClientFactory.Instance, DiscordRuntimeOptions.Default)
        {
        }

        /// <summary>
        /// Test seam: creates the runtime with a custom gateway client factory and timing options.
        /// </summary>
        internal DiscordBotRuntimeService(
            ILogger<DiscordBotRuntimeService> logger,
            ILoggerFactory loggerFactory,
            IServiceScopeFactory scopeFactory,
            IPluginRegistry pluginRegistry,
            IBoardMessageLocator boardMessageLocator,
            IBotNotificationService notificationService,
            IDiscordGatewayClientFactory clientFactory,
            DiscordRuntimeOptions options)
        {
            _clientFactory = clientFactory;
            _options = options;
            _logger = logger;
            _loggerFactory = loggerFactory;
            _scopeFactory = scopeFactory;
            _pluginRegistry = pluginRegistry;
            _boardMessageLocator = boardMessageLocator;
            _notificationService = notificationService;
        }

        /// <summary>
        /// Starts the Discord client of a bot and waits for readiness.
        /// Does nothing when the bot is already running.
        /// </summary>
        public async Task StartAsync(int botId, string botToken)
        {
            var gate = _lifecycleLocks.GetOrAdd(botId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (_sessions.TryGetValue(botId, out var existing))
                {
                    if (existing.IsRunning())
                    {
                        _logger.LogWarning("Bot {BotId} is already running", botId);
                        return;
                    }

                    // Stale session (e.g. marked offline after a prolonged disconnect): replace it.
                    _sessions.TryRemove(botId, out _);
                    await existing.DisposeAsync();
                }

                var session = new DiscordBotSession(
                    botId,
                    new BotScopedLogger(_loggerFactory.CreateLogger<DiscordBotSession>(), botId),
                    _scopeFactory,
                    _pluginRegistry,
                    _boardMessageLocator,
                    _notificationService,
                    _clientFactory,
                    _options);

                try
                {
                    await session.StartAsync(botToken);
                }
                catch
                {
                    await session.DisposeAsync();
                    throw;
                }

                _sessions[botId] = session;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Stops the Discord client of a bot and releases its resources.
        /// </summary>
        public async Task StopAsync(int botId)
        {
            var gate = _lifecycleLocks.GetOrAdd(botId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (!_sessions.TryRemove(botId, out var session))
                {
                    _logger.LogWarning("Bot {BotId} is not running", botId);
                    return;
                }

                try
                {
                    await session.StopAsync();
                }
                finally
                {
                    await session.DisposeAsync();
                    _pluginRegistry.RemovePlugin(botId);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Stops all running bots (used on application shutdown).
        /// </summary>
        public async Task StopAllAsync()
        {
            foreach (var botId in _sessions.Keys.ToList())
            {
                try
                {
                    await StopAsync(botId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to stop bot {BotId} during shutdown", botId);
                }
            }
        }

        /// <summary>
        /// Returns whether the given bot is currently connected to Discord.
        /// </summary>
        public Task<bool> IsRunningAsync(int botId)
            => Task.FromResult(_sessions.TryGetValue(botId, out var session) && session.IsRunning());

        /// <summary>
        /// Gets a textual representation of the bot's current connection state.
        /// </summary>
        public string GetStatus(int botId)
            => _sessions.TryGetValue(botId, out var session) ? session.GetStatus() : "Offline";

        /// <summary>
        /// Creates or updates the configured board message for a bot.
        /// </summary>
        public Task<bool> RefreshBoardMessageAsync(int botId, BoardMessageDto boardMessage, int? boardConfigurationId = null)
        {
            if (!_sessions.TryGetValue(botId, out var session))
            {
                _logger.LogInformation("Skipping board refresh for bot {BotId} because the Discord client is not running", botId);
                return Task.FromResult(false);
            }

            return session.RefreshBoardMessageAsync(botId, boardMessage, boardConfigurationId);
        }

        /// <summary>
        /// Rebuilds board message reactions when reactions already exist on the message.
        /// </summary>
        public Task<bool> SyncBoardReactionsIfPresentAsync(int botId, IEnumerable<string> emojis, int? boardConfigurationId = null)
        {
            if (!_sessions.TryGetValue(botId, out var session))
            {
                _logger.LogInformation("Skipping reaction sync for bot {BotId} because the Discord client is not running", botId);
                return Task.FromResult(false);
            }

            return session.SyncBoardReactionsIfPresentAsync(botId, emojis, boardConfigurationId);
        }

        /// <summary>
        /// Stops all sessions when the container is disposed.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await StopAllAsync();
            GC.SuppressFinalize(this);
        }
    }
}
