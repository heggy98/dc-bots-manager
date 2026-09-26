using Discord;
using Discord.WebSocket;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Models;
using BotManager.Backend.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Threading;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Hosts the Discord gateway connection of a single bot: client lifecycle, event subscription,
    /// connection state tracking and slash command registration. Interaction, reaction and board
    /// message work is delegated to <see cref="DiscordInteractionDispatcher"/>, <see cref="DiscordReactionDispatcher"/>
    /// and <see cref="BoardMessagePublisher"/>; status persistence to <see cref="GatewayStatusTracker"/>.
    /// Lifecycle calls (start/stop) are serialized by <see cref="DiscordBotRuntimeService"/>.
    /// </summary>
    internal sealed class DiscordBotSession : IAsyncDisposable
    {
        private IDiscordGatewayClient? _client;
        private readonly IDiscordGatewayClientFactory _clientFactory;
        private readonly DiscordRuntimeOptions _options;
        private readonly ILogger _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly BoardPluginHost _pluginHost;
        private readonly BoardMessagePublisher _boardPublisher;
        private readonly DiscordInteractionDispatcher _interactionDispatcher;
        private readonly DiscordReactionDispatcher _reactionDispatcher;
        private readonly GatewayStatusTracker _statusTracker;
        private volatile bool _isRunning = false;
        private volatile bool _isStopping = false;
        private int? _currentBotId;
        private readonly int _botId;
        private int _commandsRegistered = 0;
        private CancellationTokenSource? _cancellationTokenSource;
        private TaskCompletionSource<bool>? _readyTcs;
        private int _connectionGeneration = 0;

        /// <summary>
        /// Creates a new Discord bot session.
        /// </summary>
        public DiscordBotSession(
            int botId,
            ILogger logger,
            IServiceScopeFactory scopeFactory,
            IPluginRegistry pluginRegistry,
            IBoardMessageLocator boardMessageLocator,
            IBotNotificationService notificationService,
            IDiscordGatewayClientFactory? clientFactory = null,
            DiscordRuntimeOptions? options = null)
        {
            _botId = botId;
            _clientFactory = clientFactory ?? DiscordSocketGatewayClientFactory.Instance;
            _options = options ?? DiscordRuntimeOptions.Default;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _pluginHost = new BoardPluginHost(logger, pluginRegistry);
            _reactionDispatcher = new DiscordReactionDispatcher(botId, logger, scopeFactory, _pluginHost, HasCurrentBot, () => _client?.SocketClient);
            _interactionDispatcher = new DiscordInteractionDispatcher(botId, logger, scopeFactory, _pluginHost, HasCurrentBot);
            _boardPublisher = new BoardMessagePublisher(logger, scopeFactory, boardMessageLocator, _reactionDispatcher.InvalidateBoardMessageIds);
            _statusTracker = new GatewayStatusTracker(botId, logger, scopeFactory, notificationService, _options.DisconnectGrace);
        }

        /// <summary>
        /// Returns whether the session currently has an active bot (set during start, cleared on stop).
        /// </summary>
        private bool HasCurrentBot() => _currentBotId.HasValue;

        /// <summary>
        /// Starts the Discord client and waits for readiness before marking runtime online.
        /// </summary>
        public async Task StartAsync(string botToken)
        {
            if (_isRunning)
            {
                _logger.LogWarning("Bot is already running");
                return;
            }

            if (string.IsNullOrWhiteSpace(botToken))
            {
                _logger.LogError("Cannot start Discord bot: token is empty");
                throw new ArgumentException("Bot token is empty", nameof(botToken));
            }

            try
            {
                _currentBotId = _botId;
                _isStopping = false;
                _commandsRegistered = 0;
                _pluginHost.ResetInitialization();
                _reactionDispatcher.InvalidateBoardMessageIds();
                _cancellationTokenSource = new CancellationTokenSource();
                _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                var config = new DiscordSocketConfig
                {
                    // Only the intents the bot actually uses: guild/role data, members (role toggles)
                    // and reactions on the board message. Message content is not needed.
                    GatewayIntents =
                        GatewayIntents.Guilds |
                        GatewayIntents.GuildMessageReactions |
                        GatewayIntents.GuildMembers,
                    LogGatewayIntentWarnings = true
                };

                _client = _clientFactory.Create(config);

                _client.Log += LogAsync;
                _client.Ready += ReadyAsync;
                _client.Connected += ConnectedAsync;
                _client.Disconnected += DisconnectedAsync;
                _client.LatencyUpdated += LatencyUpdatedAsync;
                var socketClient = _client.SocketClient;
                if (socketClient != null)
                {
                    socketClient.InteractionCreated += _interactionDispatcher.HandleInteractionCreatedAsync;
                    socketClient.SlashCommandExecuted += _interactionDispatcher.HandleSlashCommandExecutedAsync;
                    socketClient.ReactionAdded += _reactionDispatcher.HandleReactionAddedEventAsync;
                    socketClient.ReactionRemoved += _reactionDispatcher.HandleReactionRemovedEventAsync;
                }
                _logger.LogInformation("Subscribed Discord event handlers including reaction role events.");

                var startupStopwatch = Stopwatch.StartNew();
                _logger.LogInformation("Starting Discord bot client. Initial state={State}", _client.ConnectionState);

                await _client.LoginAsync(TokenType.Bot, botToken);
                _logger.LogInformation("Discord LoginAsync completed. State={State}", _client.ConnectionState);

                await _client.StartAsync();
                _logger.LogInformation("Discord StartAsync completed. State={State}. Waiting for READY event (timeout {TimeoutSeconds}s)",
                    _client.ConnectionState,
                    _options.ReadyTimeout.TotalSeconds);

                var readyTask = _readyTcs.Task;
                var timeoutTask = Task.Delay(_options.ReadyTimeout, _cancellationTokenSource.Token);
                var completed = await Task.WhenAny(readyTask, timeoutTask);

                if (completed != readyTask)
                {
                    var msg = $"Discord READY event timeout after {_options.ReadyTimeout.TotalSeconds}s. Current state={_client.ConnectionState}";
                    _logger.LogError(msg);
                    throw new TimeoutException(msg);
                }

                _isRunning = true;
                _logger.LogInformation("Discord bot started successfully in {ElapsedMs} ms. State={State}",
                    startupStopwatch.ElapsedMilliseconds,
                    _client.ConnectionState);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Discord bot");
                _isRunning = false;
                await CleanupClientAfterFailedStartAsync();
                throw;
            }
        }

        /// <summary>
        /// Stops the Discord client and detaches runtime event handlers.
        /// </summary>
        public async Task StopAsync()
        {
            // The client may still exist (and keep reconnecting) even after the runtime was
            // marked not-running by a prolonged disconnect, so stop whenever a client exists.
            if (_client == null)
            {
                _logger.LogWarning("Bot is not running");
                return;
            }

            try
            {
                _isStopping = true;
                _cancellationTokenSource?.Cancel();
                await _client.StopAsync();
                await _client.LogoutAsync();
                _logger.LogInformation("Discord bot {BotId} stopped", _currentBotId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping Discord bot {BotId}", _currentBotId);
                throw;
            }
            finally
            {
                UnsubscribeClientEvents();
                DisposeClient();
                _isRunning = false;
                _currentBotId = null;
                _isStopping = false;
            }
        }

        /// <summary>
        /// Stops the session (if still running) and releases resources.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_client != null)
                {
                    await StopAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop Discord bot session during dispose");
            }

            _cancellationTokenSource?.Dispose();
            _pluginHost.Dispose();
        }

        /// <summary>
        /// Disposes the current Discord client instance so repeated restarts do not leak sockets/timers.
        /// </summary>
        private void DisposeClient()
        {
            var client = _client;
            _client = null;

            try
            {
                client?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose Discord client");
            }
        }

        /// <summary>
        /// Returns whether the runtime is currently connected to Discord.
        /// </summary>
        public bool IsRunning()
        {
            return _isRunning && _client?.ConnectionState == ConnectionState.Connected;
        }

        /// <summary>
        /// Gets a textual representation of current runtime connection state.
        /// </summary>
        public string GetStatus()
        {
            if (!_isRunning)
                return "Offline";

            if (_client == null)
                return "Offline";

            return _client.ConnectionState switch
            {
                ConnectionState.Connected => "Online",
                ConnectionState.Connecting => "Connecting",
                ConnectionState.Disconnecting => "Disconnecting",
                ConnectionState.Disconnected => "Offline",
                _ => "Unknown"
            };
        }
        /// <summary>
        /// Creates or updates the configured board message for a bot.
        /// </summary>
        public Task<bool> RefreshBoardMessageAsync(int botId, BoardMessageDto boardMessage, int? boardConfigurationId = null)
        {
            var client = _client;
            if (client == null || !_isRunning)
            {
                _logger.LogInformation("Skipping board refresh for bot {BotId} because the Discord client is not running", botId);
                return Task.FromResult(false);
            }

            if (client.SocketClient == null)
            {
                return Task.FromResult(false);
            }

            return _boardPublisher.RefreshBoardMessageAsync(client.SocketClient, botId, boardMessage, boardConfigurationId);
        }

        /// <summary>
        /// Rebuilds board message reactions when reactions already exist on the message.
        /// </summary>
        public Task<bool> SyncBoardReactionsIfPresentAsync(int botId, IEnumerable<string> emojis, int? boardConfigurationId = null)
        {
            var client = _client;
            if (client == null || !_isRunning)
            {
                _logger.LogInformation("Skipping reaction sync for bot {BotId} because the Discord client is not running", botId);
                return Task.FromResult(false);
            }

            if (client.SocketClient == null)
            {
                return Task.FromResult(false);
            }

            return _boardPublisher.SyncBoardReactionsIfPresentAsync(client.SocketClient, botId, emojis, boardConfigurationId);
        }


        /// <summary>
        /// Handles client readiness by registering commands and unblocking startup waiters.
        /// </summary>
        private async Task ReadyAsync()
        {
            if (_client == null)
            {
                return;
            }

            Interlocked.Increment(ref _connectionGeneration);
            _isRunning = true;

            _logger.LogInformation("Discord bot is ready. Bot={Username} ({UserId}), Guilds={GuildCount}, State={State}",
                _client.SocketClient?.CurrentUser?.Username,
                _client.SocketClient?.CurrentUser?.Id,
                _client.SocketClient?.Guilds.Count ?? 0,
                _client.ConnectionState);

            // Register commands in the background so the gateway handler is not blocked by
            // one REST call per guild.
            if (Interlocked.Exchange(ref _commandsRegistered, 1) == 0)
            {
                _ = RegisterCommandsSafeAsync();
            }

            _readyTcs?.TrySetResult(true);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Handles gateway connected events for diagnostics.
        /// </summary>
        private async Task ConnectedAsync()
        {
            if (_client == null)
            {
                return;
            }

            Interlocked.Increment(ref _connectionGeneration);
            if (!_isStopping)
            {
                _isRunning = true;
            }

            _logger.LogInformation("Gateway connected. State={State}, Latency={Latency}ms", _client.ConnectionState, _client.Latency);

            // After an unexpected disconnect the bot may have been marked Offline/Reconnecting in DB.
            // Restore Online status so the dashboard reflects the actual connected state.
            if (_currentBotId.HasValue && !_isStopping)
            {
                await _statusTracker.RestoreOnlineStatusIfNeededAsync();
            }
        }

        /// <summary>
        /// Handles gateway disconnections and persists offline state when disconnect is prolonged.
        /// </summary>
        private async Task DisconnectedAsync(Exception? ex)
        {
            if (_client == null)
            {
                if (ex != null)
                {
                    _logger.LogError(ex, "Gateway disconnected and client was null");
                }
                else
                {
                    _logger.LogWarning("Gateway disconnected and client was null");
                }

                await Task.CompletedTask;
                return;
            }

            if (_isStopping)
            {
                _logger.LogInformation("Gateway disconnected during intentional stop. State={State}", _client.ConnectionState);
                await Task.CompletedTask;
                return;
            }

            var isGatewayReconnect = ex is Discord.WebSocket.GatewayReconnectException;

            if (ex != null)
            {
                _logger.LogError(ex, "Gateway disconnected due to exception. State={State}, Latency={Latency}ms", _client.ConnectionState, _client.Latency);
            }
            else
            {
                _logger.LogWarning("Gateway disconnected without exception details. State={State}, Latency={Latency}ms", _client.ConnectionState, _client.Latency);
            }

            if (_isRunning && _currentBotId.HasValue)
            {
                var disconnectGeneration = Interlocked.Increment(ref _connectionGeneration);

                // For gateway-initiated reconnects, immediately mark as Reconnecting so the dashboard
                // shows the correct transient state instead of jumping straight to offline.
                if (isGatewayReconnect)
                {
                    await _statusTracker.MarkReconnectingAsync();
                }

                _ = _statusTracker.MarkBotOfflineAfterGracePeriodAsync(
                    ex,
                    disconnectGeneration,
                    isGatewayReconnect,
                    getCurrentGeneration: () => Volatile.Read(ref _connectionGeneration),
                    getConnectionState: () => _client?.ConnectionState ?? ConnectionState.Disconnected,
                    onMarkedNotRunning: () => _isRunning = false);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Handles latency change notifications and logs major spikes.
        /// </summary>
        private async Task LatencyUpdatedAsync(int oldLatency, int newLatency)
        {
            // Only log notable spikes to avoid noise.
            if (Math.Abs(newLatency - oldLatency) >= 500)
            {
                _logger.LogDebug("Gateway latency spike: {OldLatency}ms -> {NewLatency}ms", oldLatency, newLatency);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Forwards Discord.Net client logs into application logging.
        /// </summary>
        private async Task LogAsync(LogMessage msg)
        {
            var severity = msg.Severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Verbose => LogLevel.Debug,
                LogSeverity.Debug => LogLevel.Debug,
                _ => LogLevel.Information
            };

            if (msg.Exception != null)
            {
                _logger.Log(severity, msg.Exception, "{Source}: {Message}", msg.Source, msg.Message);
            }
            else
            {
                _logger.Log(severity, "{Source}: {Message}", msg.Source, msg.Message);
            }

            await Task.CompletedTask;
        }


        /// <summary>
        /// Cleans up client resources after a failed startup attempt.
        /// </summary>
        private async Task CleanupClientAfterFailedStartAsync()
        {
            try
            {
                if (_client == null)
                {
                    return;
                }

                await _client.StopAsync();
                await _client.LogoutAsync();
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to clean up Discord client after startup failure");
            }
            finally
            {
                UnsubscribeClientEvents();
                DisposeClient();
                _currentBotId = null;
                _commandsRegistered = 0;
            }
        }

        /// <summary>
        /// Runs slash command registration and resets the registration flag on failure.
        /// </summary>
        private async Task RegisterCommandsSafeAsync()
        {
            try
            {
                await RegisterCommandsViaProviderAsync();
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _commandsRegistered, 0);
                _logger.LogError(ex, "Slash command registration failed for bot {BotId}", _currentBotId);
            }
        }

        /// <summary>
        /// Registers slash commands for all connected guilds using the command provider.
        /// </summary>
        private async Task RegisterCommandsViaProviderAsync()
        {
            var socketClient = _client?.SocketClient;
            if (socketClient == null)
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var provider = scope.ServiceProvider.GetService<IDiscordCommandProvider>();
            if (provider == null)
            {
                _logger.LogWarning("IDiscordCommandProvider is not registered; skipping slash command registration");
                return;
            }

            var commands = provider.BuildCommands();

            foreach (var guild in socketClient.Guilds)
            {
                try
                {
                    await guild.BulkOverwriteApplicationCommandAsync(commands.ToArray());
                    _logger.LogInformation("Registered {CommandCount} slash commands for guild {GuildName} ({GuildId})",
                        commands.Count,
                        guild.Name,
                        guild.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to register slash commands for guild {GuildName} ({GuildId})", guild.Name, guild.Id);
                }
            }
        }


        /// <summary>
        /// Unsubscribes all Discord client event handlers.
        /// </summary>
        private void UnsubscribeClientEvents()
        {
            if (_client == null)
            {
                return;
            }

            _client.Log -= LogAsync;
            _client.Ready -= ReadyAsync;
            _client.Connected -= ConnectedAsync;
            _client.Disconnected -= DisconnectedAsync;
            _client.LatencyUpdated -= LatencyUpdatedAsync;
            var socketClient = _client.SocketClient;
            if (socketClient != null)
            {
                socketClient.InteractionCreated -= _interactionDispatcher.HandleInteractionCreatedAsync;
                socketClient.SlashCommandExecuted -= _interactionDispatcher.HandleSlashCommandExecutedAsync;
                socketClient.ReactionAdded -= _reactionDispatcher.HandleReactionAddedEventAsync;
                socketClient.ReactionRemoved -= _reactionDispatcher.HandleReactionRemovedEventAsync;
            }
        }

    }
}
