using Discord;
using Discord.WebSocket;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Models;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Threading;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Hosts Discord gateway runtime, command dispatching, and board message synchronization.
    /// </summary>
    public class DiscordBotRuntimeService : IDiscordBotService
    {
        private DiscordSocketClient? _client;
        private readonly ILogger<DiscordBotRuntimeService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IPluginRegistry _pluginRegistry;
        private readonly IBoardMessageLocator _boardMessageLocator;
        private readonly IBotNotificationService _notificationService;
        private bool _isRunning = false;
        private bool _isStopping = false;
        private int? _currentBotId;
        private bool _commandsRegistered = false;
        private bool _pluginInitialized = false;
        private CancellationTokenSource? _cancellationTokenSource;
        private TaskCompletionSource<bool>? _readyTcs;
        private int _connectionGeneration = 0;
        private const int ReadyTimeoutSeconds = 30;
        private const int DisconnectGraceSeconds = 30;
        private const string BoardMessageMarker = "\u200B";
        private const string BoardPluginId = "discord-board";

        /// <summary>
        /// Creates a new Discord runtime service.
        /// </summary>
        public DiscordBotRuntimeService(
            ILogger<DiscordBotRuntimeService> logger,
            IServiceScopeFactory scopeFactory,
            IPluginRegistry pluginRegistry,
            IBoardMessageLocator boardMessageLocator,
            IBotNotificationService notificationService)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _pluginRegistry = pluginRegistry;
            _boardMessageLocator = boardMessageLocator;
            _notificationService = notificationService;
        }

        /// <summary>
        /// Starts the Discord client and waits for readiness before marking runtime online.
        /// </summary>
        public async Task StartAsync(int botId, string botToken)
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
                _currentBotId = botId;
                _isStopping = false;
                _commandsRegistered = false;
                _pluginInitialized = false;
                _cancellationTokenSource = new CancellationTokenSource();
                _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                var config = new DiscordSocketConfig
                {
                    GatewayIntents =
                        GatewayIntents.Guilds |
                        GatewayIntents.GuildMessages |
                        GatewayIntents.MessageContent |
                        GatewayIntents.GuildMessageReactions |
                        GatewayIntents.GuildMembers,
                    LogGatewayIntentWarnings = true
                };

                _client = new DiscordSocketClient(config);

                _client.Log += LogAsync;
                _client.Ready += ReadyAsync;
                _client.Connected += ConnectedAsync;
                _client.Disconnected += DisconnectedAsync;
                _client.LatencyUpdated += LatencyUpdatedAsync;
                _client.InteractionCreated += HandleInteractionCreatedAsync;
                _client.SlashCommandExecuted += HandleSlashCommandExecutedAsync;
                _client.ReactionAdded += HandleReactionAddedEventAsync;
                _client.ReactionRemoved += HandleReactionRemovedEventAsync;
                _logger.LogInformation("Subscribed Discord event handlers including reaction role events.");

                var startupStopwatch = Stopwatch.StartNew();
                _logger.LogInformation("Starting Discord bot client. Initial state={State}", _client.ConnectionState);

                await _client.LoginAsync(TokenType.Bot, botToken);
                _logger.LogInformation("Discord LoginAsync completed. State={State}", _client.ConnectionState);

                await _client.StartAsync();
                _logger.LogInformation("Discord StartAsync completed. State={State}. Waiting for READY event (timeout {TimeoutSeconds}s)",
                    _client.ConnectionState,
                    ReadyTimeoutSeconds);

                var readyTask = _readyTcs.Task;
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(ReadyTimeoutSeconds), _cancellationTokenSource.Token);
                var completed = await Task.WhenAny(readyTask, timeoutTask);

                if (completed != readyTask)
                {
                    var msg = $"Discord READY event timeout after {ReadyTimeoutSeconds}s. Current state={_client.ConnectionState}";
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
            if (!_isRunning)
            {
                _logger.LogWarning("Bot is not running");
                return;
            }

            try
            {
                _isStopping = true;
                _cancellationTokenSource?.Cancel();
                if (_client != null)
                {
                    await _client.StopAsync();
                    await _client.LogoutAsync();
                }

                UnsubscribeClientEvents();
                _isRunning = false;
                _currentBotId = null;
                _logger.LogInformation("Discord bot alliance stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping Discord bot alliance");
                throw;
            }
            finally
            {
                _isStopping = false;
            }
        }

        /// <summary>
        /// Returns whether the runtime is currently connected to Discord.
        /// </summary>
        public async Task<bool> IsRunningAsync()
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
        public async Task<bool> RefreshBoardMessageAsync(int botId, BoardMessageDto boardMessage, int? boardConfigurationId = null)
        {
            if (_client == null || !_isRunning)
            {
                _logger.LogInformation("Skipping board refresh for bot {BotId} because the Discord client is not running", botId);
                return false;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();

                BoardConfiguration? botConfig;
                if (boardConfigurationId.HasValue)
                {
                    botConfig = await db.BoardConfigurations
                        .FirstOrDefaultAsync(c => c.BotId == botId && c.BoardConfigurationId == boardConfigurationId.Value);
                }
                else
                {
                    var botConfiguration = await db.BotConfigurations
                        .AsNoTracking()
                        .FirstOrDefaultAsync(c => c.BotId == botId);

                    if (botConfiguration?.ActiveBoardConfigurationId != null)
                    {
                        botConfig = await db.BoardConfigurations
                            .FirstOrDefaultAsync(c => c.BotId == botId && c.BoardConfigurationId == botConfiguration.ActiveBoardConfigurationId.Value);
                    }
                    else
                    {
                        botConfig = null;
                    }

                    botConfig ??= await db.BoardConfigurations
                        .Where(c => c.BotId == botId)
                        .OrderBy(c => c.BoardConfigurationId)
                        .FirstOrDefaultAsync();
                }

                if (botConfig?.BoardChannelId == null)
                {
                    _logger.LogWarning("Cannot refresh board for bot {BotId}: BoardChannelId is missing", botId);
                    return false;
                }

                var boardChannel = _client.GetChannel(botConfig.BoardChannelId.Value) as SocketTextChannel;
                if (boardChannel == null)
                {
                    _logger.LogWarning("Cannot refresh board for bot {BotId}: board channel {BoardChannelId} was not found", botId, botConfig.BoardChannelId.Value);
                    return false;
                }

                var embed = BuildBoardEmbed(boardMessage);

                IUserMessage? targetMessage = null;
                if (botConfig.BoardMessageId.HasValue)
                {
                    targetMessage = await boardChannel.GetMessageAsync(botConfig.BoardMessageId.Value) as IUserMessage;
                }

                if (targetMessage == null)
                {
                    targetMessage = await _boardMessageLocator.FindBoardMessageAsync(boardChannel, BoardMessageMarker);
                }

                var components = BuildBoardComponents(boardMessage);

                if (targetMessage != null)
                {
                    await targetMessage.ModifyAsync(m =>
                    {
                        m.Content = BoardMessageMarker;
                        m.Embed = embed.Build();
                        m.Components = components.Build();
                    });

                    botConfig.BoardMessageId = targetMessage.Id;
                }
                else
                {
                    var newMessage = await boardChannel.SendMessageAsync(BoardMessageMarker, embed: embed.Build(), components: components.Build());
                    botConfig.BoardMessageId = newMessage.Id;
                }

                await db.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh board message for bot {BotId}", botId);
                return false;
            }
        }

        /// <summary>
        /// Rebuilds board message reactions when reactions already exist on the message.
        /// </summary>
        public async Task<bool> SyncBoardReactionsIfPresentAsync(int botId, IEnumerable<string> emojis, int? boardConfigurationId = null)
        {
            if (_client == null || !_isRunning)
            {
                _logger.LogInformation("Skipping reaction sync for bot {BotId} because the Discord client is not running", botId);
                return false;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();

                var botConfig = await ResolveBoardConfigurationAsync(db, botId, boardConfigurationId);
                if (botConfig?.BoardChannelId == null)
                {
                    _logger.LogWarning("Cannot sync reactions for bot {BotId}: BoardChannelId is missing", botId);
                    return false;
                }

                var boardChannel = _client.GetChannel(botConfig.BoardChannelId.Value) as SocketTextChannel;
                if (boardChannel == null)
                {
                    _logger.LogWarning("Cannot sync reactions for bot {BotId}: board channel {BoardChannelId} was not found", botId, botConfig.BoardChannelId.Value);
                    return false;
                }

                IUserMessage? targetMessage = null;
                if (botConfig.BoardMessageId.HasValue)
                {
                    targetMessage = await boardChannel.GetMessageAsync(botConfig.BoardMessageId.Value) as IUserMessage;
                }

                if (targetMessage == null)
                {
                    targetMessage = await _boardMessageLocator.FindBoardMessageAsync(boardChannel, BoardMessageMarker);
                }

                if (targetMessage == null)
                {
                    _logger.LogInformation("Skipping reaction sync for bot {BotId}: board message was not found", botId);
                    return false;
                }

                if (targetMessage.Reactions.Count == 0)
                {
                    _logger.LogInformation("Skipping reaction sync for bot {BotId}: board message has no reactions", botId);
                    return false;
                }

                await targetMessage.RemoveAllReactionsAsync();

                var uniqueEmojis = emojis
                    .Select(e => string.IsNullOrWhiteSpace(e) ? "🎯" : e.Trim())
                    .Distinct(StringComparer.Ordinal);

                foreach (var emoji in uniqueEmojis)
                {
                    if (Emoji.TryParse(emoji, out var parsedEmoji))
                    {
                        await targetMessage.AddReactionAsync(parsedEmoji);
                    }
                }

                botConfig.BoardMessageId = targetMessage.Id;
                await db.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync board reactions for bot {BotId}", botId);
                return false;
            }
        }

        /// <summary>
        /// Resolves a target board configuration for optional board scoping.
        /// </summary>
        private static async Task<BoardConfiguration?> ResolveBoardConfigurationAsync(BotManagerDbContext db, int botId, int? boardConfigurationId)
        {
            if (boardConfigurationId.HasValue)
            {
                return await db.BoardConfigurations
                    .FirstOrDefaultAsync(c => c.BotId == botId && c.BoardConfigurationId == boardConfigurationId.Value);
            }

            var botConfiguration = await db.BotConfigurations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.BotId == botId);

            if (botConfiguration?.ActiveBoardConfigurationId != null)
            {
                var activeConfig = await db.BoardConfigurations
                    .FirstOrDefaultAsync(c => c.BotId == botId && c.BoardConfigurationId == botConfiguration.ActiveBoardConfigurationId.Value);

                if (activeConfig != null)
                {
                    return activeConfig;
                }
            }

            return await db.BoardConfigurations
                .Where(c => c.BotId == botId)
                .OrderBy(c => c.BoardConfigurationId)
                .FirstOrDefaultAsync();
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
                _client.CurrentUser?.Username,
                _client.CurrentUser?.Id,
                _client.Guilds.Count,
                _client.ConnectionState);

            if (!_commandsRegistered)
            {
                await RegisterCommandsViaProviderAsync();
                _commandsRegistered = true;
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
                await RestoreOnlineStatusIfNeededAsync(_currentBotId.Value);
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
                    await SetBotStatusInDbAsync(_currentBotId.Value, BotStatus.Reconnecting);
                    await _notificationService.NotifyBotStatusChangedAsync(_currentBotId.Value, BotStatus.Reconnecting);
                }

                _ = MarkBotOfflineAfterGracePeriodAsync(_currentBotId.Value, ex, disconnectGeneration, isGatewayReconnect);
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
        /// Prefix used in button custom IDs for team toggle interactions.
        /// </summary>
        internal const string TeamTogglePrefix = "team_toggle:";

        /// <summary>
        /// Prefix used in select-menu custom IDs for team toggle interactions.
        /// </summary>
        internal const string TeamSelectPrefix = "team_select:";

        /// <summary>
        /// Prefix used in button custom IDs for board admin action buttons.
        /// </summary>
        internal const string BoardActionPrefix = "board_action:";

        /// <summary>Custom ID of the Add Team admin button.</summary>
        internal const string BoardAddTeamActionId = "board_action:add_team";

        /// <summary>Custom ID of the Refresh Board admin button.</summary>
        internal const string BoardRefreshActionId = "board_action:refresh";

        /// <summary>Prefix used in modal custom IDs for board modal submissions.</summary>
        internal const string BoardModalPrefix = "board_modal:";

        /// <summary>Custom ID of the Add Team modal.</summary>
        internal const string BoardAddTeamModalId = "board_modal:add_team";

        /// <summary>
        /// Builds the message components for the board message.
        /// Rows 0-3 hold team select menus (up to 100 teams total).
        /// Row 4 is reserved for admin action buttons (Add Team, Refresh Board).
        /// Picking a team from a menu toggles the user's membership in that team role.
        /// </summary>
        private static ComponentBuilder BuildBoardComponents(BoardMessageDto boardMessage)
        {
            var builder = new ComponentBuilder();
            const int maxTeamMenus = 4;      // rows 0-3
            const int maxOptionsPerMenu = 25;
            const int maxTeamsTotal = maxTeamMenus * maxOptionsPerMenu; // 100
            const int maxTeamNameValueLength = 100;
            const int maxLabelLength = 100;

            var entries = boardMessage.Entries.Take(maxTeamsTotal).ToList();
            var menuCount = (int)Math.Ceiling(entries.Count / (double)maxOptionsPerMenu);

            for (var menuIndex = 0; menuIndex < menuCount; menuIndex++)
            {
                var menuEntries = entries
                    .Skip(menuIndex * maxOptionsPerMenu)
                    .Take(maxOptionsPerMenu)
                    .ToList();

                var select = new SelectMenuBuilder()
                    .WithCustomId($"{TeamSelectPrefix}{menuIndex}")
                    .WithPlaceholder($"Choose your team ({menuIndex + 1}/{menuCount})")
                    .WithMinValues(1)
                    .WithMaxValues(1);

                foreach (var entry in menuEntries)
                {
                    var teamValue = entry.Title.Length > maxTeamNameValueLength
                        ? entry.Title[..maxTeamNameValueLength]
                        : entry.Title;
                    var label = entry.Title.Length > maxLabelLength
                        ? entry.Title[..maxLabelLength]
                        : entry.Title;

                    var option = new SelectMenuOptionBuilder()
                        .WithLabel(label)
                        .WithValue(teamValue);

                    if (!string.IsNullOrWhiteSpace(entry.Emoji) && Emoji.TryParse(entry.Emoji.Trim(), out var parsedEmoji))
                    {
                        option.WithEmote(parsedEmoji);
                    }

                    select.AddOption(option);
                }

                builder.WithSelectMenu(select, row: menuIndex);
            }

            // Admin action row — always on row 4.
            builder.WithButton(new ButtonBuilder()
                .WithLabel("Add Team")
                .WithCustomId(BoardAddTeamActionId)
                .WithStyle(ButtonStyle.Success)
                .WithEmote(new Emoji("➕")), row: 4);

            builder.WithButton(new ButtonBuilder()
                .WithLabel("Refresh Board")
                .WithCustomId(BoardRefreshActionId)
                .WithStyle(ButtonStyle.Secondary)
                .WithEmote(new Emoji("🔄")), row: 4);

            return builder;
        }

        /// <summary>
        /// Builds the Discord modal shown when an admin clicks the Add Team button.
        /// </summary>
        private static Modal BuildAddTeamModal()
        {
            return new ModalBuilder()
                .WithTitle("Add New Team")
                .WithCustomId(BoardAddTeamModalId)
                .AddTextInput("Team Name", "team_name", TextInputStyle.Short,
                    placeholder: "e.g. Alpha Squad", required: true, maxLength: 100)
                .AddTextInput("Leader Name", "leader_name", TextInputStyle.Short,
                    placeholder: "e.g. John Doe", required: true, maxLength: 100)
                .AddTextInput("Contact", "contact_info", TextInputStyle.Short,
                    placeholder: "e.g. @johndoe or #channel", required: false, maxLength: 100)
                .Build();
        }

        /// <summary>
        /// Builds the embed payload used for board message publishing.
        /// </summary>
        private static EmbedBuilder BuildBoardEmbed(BoardMessageDto boardMessage)
        {
            var embed = new EmbedBuilder()
                .WithTitle(string.IsNullOrWhiteSpace(boardMessage.Title) ? "📋 Seznam položek" : boardMessage.Title)
                .WithColor(Color.Blue)
                .WithFooter($"Aktualizováno: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");

            if (!string.IsNullOrWhiteSpace(boardMessage.Description))
            {
                embed.WithDescription(boardMessage.Description);
            }

            foreach (var entry in boardMessage.Entries)
            {
                var emoji = string.IsNullOrWhiteSpace(entry.Emoji) ? string.Empty : entry.Emoji.Trim() + " ";
                var details = string.IsNullOrWhiteSpace(entry.Details) ? "\u200B" : entry.Details.Trim();
                // Add one visual spacer line between teams.
                embed.AddField($"{emoji}{entry.Title}", $"{details}\n\u200B", inline: false);
            }

            return embed;
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
                _currentBotId = null;
                _commandsRegistered = false;
            }
        }

        /// <summary>
        /// Registers slash commands for all connected guilds using the command provider.
        /// </summary>
        private async Task RegisterCommandsViaProviderAsync()
        {
            if (_client == null)
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

            foreach (var guild in _client.Guilds)
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
        /// Marks a bot offline in storage after a disconnect grace period expires.
        /// </summary>
        private async Task MarkBotOfflineAfterGracePeriodAsync(int botId, Exception? ex, int disconnectGeneration, bool wasGatewayReconnect = false)
        {
            await Task.Delay(TimeSpan.FromSeconds(DisconnectGraceSeconds));

            var currentGeneration = Volatile.Read(ref _connectionGeneration);
            var connectionState = _client?.ConnectionState ?? ConnectionState.Disconnected;

            if (!GatewayDisconnectPolicy.ShouldMarkOffline(disconnectGeneration, currentGeneration, connectionState, persistedStatus: null))
            {
                _logger.LogInformation("Skipping offline transition for bot {BotId}: reconnect state is already healthy or superseded.", botId);
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();

                var bot = await db.Bots.FindAsync(botId);
                if (bot == null)
                {
                    _logger.LogWarning("Could not persist unexpected disconnect state because bot {BotId} was not found", botId);
                    _isRunning = false;
                    return;
                }

                if (!GatewayDisconnectPolicy.ShouldMarkOffline(disconnectGeneration, Volatile.Read(ref _connectionGeneration), _client?.ConnectionState ?? ConnectionState.Disconnected, bot.Status))
                {
                    _logger.LogInformation("Bot {BotId} is already back Online — skipping offline transition.", botId);
                    return;
                }

                var openHistory = await db.BotRunHistories
                    .Where(h => h.BotId == botId && h.StoppedAt == null)
                    .OrderByDescending(h => h.StartedAt)
                    .FirstOrDefaultAsync();

                var stopAt = DateTime.UtcNow;
                var reason = wasGatewayReconnect
                    ? $"Gateway reconnect failed (>{DisconnectGraceSeconds}s timeout)"
                    : $"Neočekávaný gateway disconnect (>{DisconnectGraceSeconds}s)";
                var details = ex?.ToString() ?? "Gateway disconnected without exception details.";

                bot.Status = BotStatus.Offline;
                bot.LastStoppedAt = stopAt;

                if (openHistory != null)
                {
                    openHistory.StoppedAt = stopAt;
                    openHistory.DurationSeconds = (long)(openHistory.StoppedAt.Value - openHistory.StartedAt).TotalSeconds;
                    openHistory.StopReason = Truncate(reason, 500);
                    openHistory.ErrorDetails = Truncate(details, 2000);
                }

                await db.SaveChangesAsync();
                _isRunning = false;

                _logger.LogWarning("Persisted unexpected disconnect to DB for bot {BotId}. Reason={Reason}", botId, reason);
                await _notificationService.NotifyBotStatusChangedAsync(botId, BotStatus.Offline);
                await _notificationService.NotifyHistoryUpdatedAsync(botId);
            }
            catch (Exception persistEx)
            {
                _logger.LogError(persistEx, "Failed to persist unexpected disconnect state for bot {BotId}", botId);
            }
        }

        /// <summary>
        /// Restores bot status to Online in DB when it reconnected after an unexpected disconnect.
        /// Creates a new run-history entry if needed.
        /// </summary>
        private async Task RestoreOnlineStatusIfNeededAsync(int botId)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();

                var bot = await db.Bots.FindAsync(botId);
                if (bot == null) return;

                if (bot.Status is not (BotStatus.Offline or BotStatus.Reconnecting)) return;

                bot.Status = BotStatus.Online;
                bot.LastStartedAt = DateTime.UtcNow;
                bot.LastStoppedAt = null;

                // Open a fresh history entry for the resumed session.
                db.BotRunHistories.Add(new BotRunHistory { BotId = botId, StartedAt = DateTime.UtcNow });

                await db.SaveChangesAsync();

                _logger.LogInformation("Restored bot {BotId} to Online after reconnect.", botId);
                await _notificationService.NotifyBotStatusChangedAsync(botId, BotStatus.Online);
                await _notificationService.NotifyHistoryUpdatedAsync(botId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore Online status for bot {BotId} after reconnect", botId);
            }
        }

        /// <summary>
        /// Updates only the bot's Status in DB without touching history or other fields.
        /// </summary>
        private async Task SetBotStatusInDbAsync(int botId, BotStatus status)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();

                var bot = await db.Bots.FindAsync(botId);
                if (bot == null) return;

                bot.Status = status;
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set bot {BotId} status to {Status}", botId, status);
            }
        }

        /// <summary>
        /// Truncates long values to a target maximum length.
        /// </summary>
        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength);
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
            _client.InteractionCreated -= HandleInteractionCreatedAsync;
            _client.SlashCommandExecuted -= HandleSlashCommandExecutedAsync;
            _client.ReactionAdded -= HandleReactionAddedEventAsync;
            _client.ReactionRemoved -= HandleReactionRemovedEventAsync;
        }

        /// <summary>
        /// Receives interactions at the earliest gateway stage, acknowledges slash commands quickly,
        /// and fire-and-forgets team-toggle button interactions.
        /// </summary>
        private async Task HandleInteractionCreatedAsync(SocketInteraction interaction)
        {
            try
            {
                if (interaction is SocketMessageComponent component)
                {
                    // Team toggle controls (select menu, legacy button) — defer ephemerally then process.
                    if (component.Data.CustomId.StartsWith(TeamSelectPrefix, StringComparison.Ordinal)
                        || component.Data.CustomId.StartsWith(TeamTogglePrefix, StringComparison.Ordinal))
                    {
                        if (await EnsureComponentDeferredAsync(component, component.Data.CustomId))
                        {
                            _ = HandleButtonInteractionInternalAsync(component);
                        }

                        return;
                    }

                    // "Add Team" button — the modal IS the initial response; cannot defer first.
                    if (component.Data.CustomId == BoardAddTeamActionId)
                    {
                        await TryShowModalAsync(component, BuildAddTeamModal());
                        return;
                    }

                    // Other board action buttons (e.g. Refresh Board) — defer ephemerally then process.
                    if (component.Data.CustomId.StartsWith(BoardActionPrefix, StringComparison.Ordinal))
                    {
                        if (await EnsureComponentDeferredAsync(component, component.Data.CustomId))
                        {
                            _ = HandleBoardActionButtonInternalAsync(component);
                        }

                        return;
                    }
                }

                // Modal submissions (e.g. the Add Team form).
                if (interaction is SocketModal modal &&
                    modal.Data.CustomId.StartsWith(BoardModalPrefix, StringComparison.Ordinal))
                {
                    if (await EnsureModalDeferredAsync(modal, modal.Data.CustomId))
                    {
                        _ = HandleBoardModalInternalAsync(modal);
                    }

                    return;
                }

                if (interaction is not SocketSlashCommand slashCommand)
                {
                    return;
                }

                var interactionAgeAtHandlerStartMs = Math.Max(0L, (long)(DateTimeOffset.UtcNow - slashCommand.CreatedAt).TotalMilliseconds);
                var deferResult = await EnsureInteractionDeferredAsync(slashCommand);

                _logger.LogInformation(
                    "InteractionCreated defer for {CommandName} outcome={Outcome} acknowledged={Acknowledged} deferElapsedMs={DeferElapsedMs} interactionAgeAtDeferMs={InteractionAgeAtDeferMs}",
                    slashCommand.CommandName,
                    deferResult.Outcome,
                    deferResult.IsAcknowledged,
                    deferResult.AttemptElapsedMs,
                    interactionAgeAtHandlerStartMs + deferResult.AttemptElapsedMs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InteractionCreated handler swallowed exception for interaction type {InteractionType}", interaction.Type);
            }
        }

        /// <summary>
        /// Attempts to defer a message component interaction and normalizes expected timeout/duplicate-ack failures.
        /// </summary>
        private async Task<bool> EnsureComponentDeferredAsync(SocketMessageComponent component, string source)
        {
            if (component.HasResponded)
            {
                return true;
            }

            try
            {
                await component.DeferAsync(ephemeral: true);
                return true;
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "Timed out while deferring component interaction {Source}", source);
                return component.HasResponded;
            }
            catch (Discord.Net.HttpException ex) when ((int?)ex.DiscordCode == 40060 || (int?)ex.DiscordCode == 10062)
            {
                return component.HasResponded;
            }
        }

        /// <summary>
        /// Attempts to defer a modal interaction and normalizes expected timeout/duplicate-ack failures.
        /// </summary>
        private async Task<bool> EnsureModalDeferredAsync(SocketModal modal, string source)
        {
            if (modal.HasResponded)
            {
                return true;
            }

            try
            {
                await modal.DeferAsync(ephemeral: true);
                return true;
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "Timed out while deferring modal interaction {Source}", source);
                return modal.HasResponded;
            }
            catch (Discord.Net.HttpException ex) when ((int?)ex.DiscordCode == 40060 || (int?)ex.DiscordCode == 10062)
            {
                return modal.HasResponded;
            }
        }

        /// <summary>
        /// Attempts to respond to a component with a modal, swallowing duplicate-ack and timeout cases.
        /// </summary>
        private async Task TryShowModalAsync(SocketMessageComponent component, Modal modal)
        {
            try
            {
                await component.RespondWithModalAsync(modal);
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "Timed out while responding with modal for component {CustomId}", component.Data.CustomId);
            }
            catch (Discord.Net.HttpException ex) when ((int?)ex.DiscordCode == 40060 || (int?)ex.DiscordCode == 10062)
            {
                // Already acknowledged/expired interaction; no further response possible.
            }
        }

        /// <summary>
        /// Resolves plugin context and dispatches a board admin action button click (e.g. Refresh Board).
        /// </summary>
        private async Task HandleBoardActionButtonInternalAsync(SocketMessageComponent component)
        {
            if (!_currentBotId.HasValue)
            {
                return;
            }

            if (component.User is not SocketGuildUser guildUser || guildUser.IsBot)
            {
                return;
            }

            var guild = guildUser.Guild;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;
                var db = serviceProvider.GetRequiredService<BotManagerDbContext>();

                var bot = await db.Bots.FindAsync(_currentBotId.Value);
                if (bot == null)
                {
                    return;
                }

                var setup = TryPreparePluginContext(serviceProvider, bot, db, $"board_action:{component.Data.CustomId}");
                if (!setup.Success || setup.Plugin == null || setup.PluginContext == null)
                {
                    return;
                }

                if (!_pluginInitialized)
                {
                    await setup.Plugin.InitializeAsync(setup.PluginContext);
                    _pluginInitialized = true;
                }

                await setup.Plugin.HandleBoardActionButtonAsync(component, guild, guildUser, setup.PluginContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while dispatching board action button {CustomId}", component.Data.CustomId);
            }
        }

        /// <summary>
        /// Resolves plugin context and dispatches a board modal submission (e.g. Add Team form).
        /// </summary>
        private async Task HandleBoardModalInternalAsync(SocketModal modal)
        {
            if (!_currentBotId.HasValue)
            {
                return;
            }

            if (modal.User is not SocketGuildUser guildUser || guildUser.IsBot)
            {
                return;
            }

            var guild = guildUser.Guild;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;
                var db = serviceProvider.GetRequiredService<BotManagerDbContext>();

                var bot = await db.Bots.FindAsync(_currentBotId.Value);
                if (bot == null)
                {
                    return;
                }

                var setup = TryPreparePluginContext(serviceProvider, bot, db, $"board_modal:{modal.Data.CustomId}");
                if (!setup.Success || setup.Plugin == null || setup.PluginContext == null)
                {
                    return;
                }

                if (!_pluginInitialized)
                {
                    await setup.Plugin.InitializeAsync(setup.PluginContext);
                    _pluginInitialized = true;
                }

                await setup.Plugin.HandleBoardModalAsync(modal, guild, guildUser, setup.PluginContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while dispatching board modal {CustomId}", modal.Data.CustomId);
            }
        }

        /// <summary>
        /// Resolves plugin context and dispatches a team-toggle button click to the active plugin.
        /// </summary>
        private async Task HandleButtonInteractionInternalAsync(SocketMessageComponent component)
        {
            if (!_currentBotId.HasValue)
            {
                return;
            }

            if (component.User is not SocketGuildUser guildUser || guildUser.IsBot)
            {
                return;
            }

            var guild = guildUser.Guild;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;
                var db = serviceProvider.GetRequiredService<BotManagerDbContext>();

                var bot = await db.Bots.FindAsync(_currentBotId.Value);
                if (bot == null)
                {
                    return;
                }

                var setup = TryPreparePluginContext(serviceProvider, bot, db, "button:team_toggle");
                if (!setup.Success || setup.Plugin == null || setup.PluginContext == null)
                {
                    return;
                }

                if (!_pluginInitialized)
                {
                    await setup.Plugin.InitializeAsync(setup.PluginContext);
                    _pluginInitialized = true;
                }

                await setup.Plugin.HandleButtonInteractionAsync(component, guild, guildUser, setup.PluginContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while dispatching button interaction {CustomId}", component.Data.CustomId);
            }
        }

        /// <summary>
        /// Receives reaction-added events and forwards them for plugin processing.
        /// </summary>
        private Task HandleReactionAddedEventAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            Cacheable<IMessageChannel, ulong> cachedChannel,
            SocketReaction reaction)
        {
            _ = HandleReactionEventInternalAsync(
                dispatchType: ReactionDispatchType.Added,
                cachedMessage: cachedMessage,
                cachedChannel: cachedChannel,
                reaction: reaction);

            return Task.CompletedTask;
        }

            /// <summary>
            /// Receives reaction-removed events and forwards them for plugin processing.
            /// </summary>
        private Task HandleReactionRemovedEventAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            Cacheable<IMessageChannel, ulong> cachedChannel,
            SocketReaction reaction)
        {
            _ = HandleReactionEventInternalAsync(
                dispatchType: ReactionDispatchType.Removed,
                cachedMessage: cachedMessage,
                cachedChannel: cachedChannel,
                reaction: reaction);

            return Task.CompletedTask;
        }

            /// <summary>
            /// Handles reaction events by resolving bot/plugin context and invoking plugin handlers.
            /// </summary>
        private async Task HandleReactionEventInternalAsync(
            ReactionDispatchType dispatchType,
            Cacheable<IUserMessage, ulong> cachedMessage,
            Cacheable<IMessageChannel, ulong> cachedChannel,
            SocketReaction reaction)
        {
            if (!_currentBotId.HasValue)
            {
                return;
            }

            if (_client?.CurrentUser != null && reaction.UserId == _client.CurrentUser.Id)
            {
                return;
            }

            try
            {
                var rawChannel = await cachedChannel.GetOrDownloadAsync();
                if (rawChannel is not ISocketMessageChannel socketChannel)
                {
                    return;
                }

                if (socketChannel is not SocketGuildChannel guildChannel)
                {
                    return;
                }

                var guild = guildChannel.Guild;
                var user = guild.GetUser(reaction.UserId);
                if (user == null || user.IsBot)
                {
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;
                var db = serviceProvider.GetRequiredService<BotManagerDbContext>();

                var bot = await db.Bots.FindAsync(_currentBotId.Value);
                if (bot == null)
                {
                    return;
                }

                var setup = TryPreparePluginContext(serviceProvider, bot, db, $"reaction:{dispatchType}");
                if (!setup.Success || setup.Plugin == null || setup.PluginContext == null)
                {
                    return;
                }

                if (!_pluginInitialized)
                {
                    await setup.Plugin.InitializeAsync(setup.PluginContext);
                    _pluginInitialized = true;
                }

                if (dispatchType == ReactionDispatchType.Added)
                {
                    await setup.Plugin.HandleReactionAddedAsync(cachedMessage, socketChannel, reaction, guild, user, setup.PluginContext);
                }
                else
                {
                    await setup.Plugin.HandleReactionRemovedAsync(cachedMessage, socketChannel, reaction, guild, user, setup.PluginContext);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while dispatching reaction event {DispatchType}", dispatchType);
            }
        }

        /// <summary>
        /// Handles slash command execution by preparing plugin context and dispatching the command.
        /// </summary>
        private async Task HandleSlashCommandExecutedAsync(SocketSlashCommand command)
        {
            var interactionAgeAtHandlerStartMs = Math.Max(0L, (long)(DateTimeOffset.UtcNow - command.CreatedAt).TotalMilliseconds);
            var deferResult = await EnsureInteractionDeferredAsync(command);

            _logger.LogInformation(
                "Slash command {CommandName} defer outcome={Outcome} acknowledged={Acknowledged} deferElapsedMs={DeferElapsedMs} interactionAgeAtDeferMs={InteractionAgeAtDeferMs}",
                command.CommandName,
                deferResult.Outcome,
                deferResult.IsAcknowledged,
                deferResult.AttemptElapsedMs,
                interactionAgeAtHandlerStartMs + deferResult.AttemptElapsedMs);

            // Do not block the Discord gateway task with long-running command work.
            _ = HandleSlashCommandExecutedCoreAsync(command);
        }

        /// <summary>
        /// Handles slash command execution by preparing plugin context and dispatching the command.
        /// </summary>
        private async Task HandleSlashCommandExecutedCoreAsync(SocketSlashCommand command)
        {
            if (!_currentBotId.HasValue)
            {
                _logger.LogWarning("Received slash command {CommandName} but no current bot id is set", command.CommandName);
                await SendInteractionMessageAsync(command, "Bot není správně inicializován (missing bot id).");
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;
                var db = serviceProvider.GetRequiredService<BotManagerDbContext>();

                var bot = await db.Bots.FindAsync(_currentBotId.Value);
                if (bot == null)
                {
                    _logger.LogWarning("Received slash command {CommandName} but bot {BotId} not found", command.CommandName, _currentBotId.Value);
                    await SendInteractionMessageAsync(command, "Bot nenalezen v databázi.");
                    return;
                }

                var setup = TryPreparePluginContext(serviceProvider, bot, db, command.CommandName);
                if (!setup.Success || setup.Plugin == null || setup.PluginContext == null)
                {
                    await SendInteractionMessageAsync(command, setup.UserMessage);
                    return;
                }

                if (!_pluginInitialized)
                {
                    await setup.Plugin.InitializeAsync(setup.PluginContext);
                    _pluginInitialized = true;
                }

                var handled = await setup.Plugin.HandleCommandAsync(command, setup.PluginContext);
                if (!handled)
                {
                    _logger.LogWarning("Command {CommandName} was not handled by plugin", command.CommandName);
                    await SendInteractionMessageAsync(command, "Příkaz nebyl pluginem zpracován.");
                }
            }
            catch (Discord.Net.HttpException ex) when ((int?)ex.DiscordCode == 10015 || (int?)ex.DiscordCode == 10062)
            {
                _logger.LogWarning(ex,
                    "Interaction token expired while handling slash command {CommandName}. The command may have completed but Discord cannot accept follow-up messages.",
                    command.CommandName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while dispatching slash command {CommandName} to plugin", command.CommandName);
                await SendInteractionMessageAsync(command, "Nastala chyba při zpracování příkazu.");
            }
        }

        /// <summary>
        /// Prepares plugin and plugin context for command and reaction dispatch operations.
        /// </summary>
        private SlashPluginSetupResult TryPreparePluginContext(
            IServiceProvider serviceProvider,
            Bot bot,
            BotManagerDbContext db,
            string commandName)
        {
            try
            {
                var plugin = _pluginRegistry.GetOrCreatePlugin(bot.BotId, BoardPluginId);
                if (plugin == null)
                {
                    _logger.LogError("Plugin instance creation failed for bot {BotId}. Command={CommandName}", bot.BotId, commandName);
                    return SlashPluginSetupResult.Fail("Nepodařilo se načíst plugin.");
                }

                var pluginContextFactory = serviceProvider.GetRequiredService<IPluginContextFactory>();
                var pluginContext = pluginContextFactory.Create(bot, db, _logger, serviceProvider);

                return SlashPluginSetupResult.Ok(plugin, pluginContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to prepare slash plugin context for command {CommandName}", commandName);
                return SlashPluginSetupResult.Fail("Chyba při přípravě plugin kontextu.");
            }
        }

        private enum ReactionDispatchType
        {
            Added,
            Removed
        }

        /// <summary>
        /// Acknowledges slash interaction early to keep within Discord's 3-second response window.
        /// </summary>
        private async Task<InteractionDeferResult> EnsureInteractionDeferredAsync(SocketSlashCommand command)
        {
            var stopwatch = Stopwatch.StartNew();

            if (command.HasResponded)
            {
                return new InteractionDeferResult(IsAcknowledged: true, Outcome: "already-responded", AttemptElapsedMs: stopwatch.ElapsedMilliseconds);
            }

            try
            {
                await command.DeferAsync(ephemeral: true);
                return new InteractionDeferResult(IsAcknowledged: true, Outcome: "deferred", AttemptElapsedMs: stopwatch.ElapsedMilliseconds);
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "Timed out while deferring interaction for command {CommandName}", command.CommandName);
                return new InteractionDeferResult(IsAcknowledged: command.HasResponded, Outcome: "timeout", AttemptElapsedMs: stopwatch.ElapsedMilliseconds);
            }
            catch (Discord.Net.HttpException ex) when ((int?)ex.DiscordCode == 40060 || (int?)ex.DiscordCode == 10062)
            {
                // Interaction was already acknowledged by another branch.
                return new InteractionDeferResult(IsAcknowledged: command.HasResponded, Outcome: $"http-{ex.DiscordCode}", AttemptElapsedMs: stopwatch.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Sends an interaction response or follow-up depending on command response state.
        /// </summary>
        private static async Task SendInteractionMessageAsync(SocketSlashCommand command, string message)
        {
            try
            {
                if (command.HasResponded)
                {
                    await command.FollowupAsync(message, ephemeral: true);
                }
                else
                {
                    await command.RespondAsync(message, ephemeral: true);
                }
            }
            catch (Discord.Net.HttpException ex) when ((int?)ex.DiscordCode == 40060)
            {
                // Interaction was already acknowledged by another branch; send as follow-up instead.
                await command.FollowupAsync(message, ephemeral: true);
            }
            catch (Discord.Net.HttpException ex) when ((int?)ex.DiscordCode == 10015 || (int?)ex.DiscordCode == 10062)
            {
                // Interaction webhook/token is no longer valid; nothing can be sent to Discord at this point.
            }
            catch (TimeoutException)
            {
                // Discord's 3-second response window was missed; command work may still have completed.
            }
        }

        /// <summary>
        /// Result envelope for plugin setup and contextual error messaging.
        /// </summary>
        private sealed record SlashPluginSetupResult(bool Success, IDiscordBotPlugin? Plugin, IPluginContext? PluginContext, string UserMessage)
        {
            /// <summary>
            /// Creates a successful plugin setup result.
            /// </summary>
            public static SlashPluginSetupResult Ok(IDiscordBotPlugin plugin, IPluginContext pluginContext)
                => new(true, plugin, pluginContext, string.Empty);

            /// <summary>
            /// Creates a failed plugin setup result with a user-facing message.
            /// </summary>
            public static SlashPluginSetupResult Fail(string userMessage)
                => new(false, null, null, userMessage);
        }

        /// <summary>
        /// Captures defer attempt telemetry for slash-command interactions.
        /// </summary>
        private sealed record InteractionDeferResult(bool IsAcknowledged, string Outcome, long AttemptElapsedMs);
    }
}
