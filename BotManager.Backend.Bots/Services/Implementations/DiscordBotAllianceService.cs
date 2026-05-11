using Discord;
using Discord.WebSocket;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Models;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Reflection;

namespace BotManager.Backend.Bots.Services.Implementations
{
    public class DiscordBotAllianceService : IDiscordBotService
    {
        private DiscordSocketClient? _client;
        private readonly ILogger<DiscordBotAllianceService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private bool _isRunning = false;
        private bool _isStopping = false;
        private int? _currentBotId;
        private bool _commandsRegistered = false;
        private bool _pluginInitialized = false;
        private CancellationTokenSource? _cancellationTokenSource;
        private TaskCompletionSource<bool>? _readyTcs;
        private const int ReadyTimeoutSeconds = 30;
        private const int DisconnectGraceSeconds = 20;
        private const string BoardMessageMarker = "[DBA_BOARD]";

        public DiscordBotAllianceService(
            ILogger<DiscordBotAllianceService> logger,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

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
                _client.SlashCommandExecuted += HandleSlashCommandExecutedAsync;

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

        public async Task<bool> IsRunningAsync()
        {
            return _isRunning && _client?.ConnectionState == ConnectionState.Connected;
        }

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

        public async Task<bool> RefreshBoardMessageAsync(int botId, BoardMessageDto boardMessage)
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

                var botConfig = await db.BotConfigurations
                    .FirstOrDefaultAsync(c => c.BotId == botId);

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
                    targetMessage = await FindBoardMessageAsync(boardChannel);
                }

                if (targetMessage != null)
                {
                    await targetMessage.ModifyAsync(m =>
                    {
                        m.Content = BoardMessageMarker;
                        m.Embed = embed.Build();
                    });

                    botConfig.BoardMessageId = targetMessage.Id;
                }
                else
                {
                    var newMessage = await boardChannel.SendMessageAsync(BoardMessageMarker, embed: embed.Build());
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

        private async Task ReadyAsync()
        {
            if (_client == null)
            {
                return;
            }

            _logger.LogInformation("Discord bot alliance is ready. Bot={Username} ({UserId}), Guilds={GuildCount}, State={State}",
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

        private async Task ConnectedAsync()
        {
            if (_client == null)
            {
                return;
            }

            _logger.LogInformation("Gateway connected. State={State}, Latency={Latency}ms", _client.ConnectionState, _client.Latency);
            await Task.CompletedTask;
        }

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
                await MarkBotOfflineAfterGracePeriodAsync(_currentBotId.Value, ex);
            }

            await Task.CompletedTask;
        }

        private async Task LatencyUpdatedAsync(int oldLatency, int newLatency)
        {
            // Only log notable spikes to avoid noise.
            if (Math.Abs(newLatency - oldLatency) >= 500)
            {
                _logger.LogWarning("Gateway latency spike: {OldLatency}ms -> {NewLatency}ms", oldLatency, newLatency);
            }

            await Task.CompletedTask;
        }

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
                embed.AddField($"{emoji}{entry.Title}", string.IsNullOrWhiteSpace(entry.Details) ? string.Empty : entry.Details, inline: false);
            }

            return embed;
        }

        private static async Task<IUserMessage?> FindBoardMessageAsync(SocketTextChannel channel)
        {
            var messages = await channel.GetMessagesAsync(limit: 100).FlattenAsync();
            return messages
                .OfType<IUserMessage>()
                .FirstOrDefault(m => string.Equals(m.Content, BoardMessageMarker, StringComparison.Ordinal));
        }

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

        private async Task MarkBotOfflineAfterGracePeriodAsync(int botId, Exception? ex)
        {
            await Task.Delay(TimeSpan.FromSeconds(DisconnectGraceSeconds));

            if (_client?.ConnectionState == ConnectionState.Connected)
            {
                _logger.LogInformation("Gateway reconnected within grace period, keeping bot {BotId} online", botId);
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

                var openHistory = await db.BotHistories
                    .Where(h => h.BotId == botId && h.StoppedAt == null)
                    .OrderByDescending(h => h.StartedAt)
                    .FirstOrDefaultAsync();

                var stopAt = DateTime.UtcNow;
                var reason = $"Neočekávaný gateway disconnect (>{DisconnectGraceSeconds}s)";
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
            }
            catch (Exception persistEx)
            {
                _logger.LogError(persistEx, "Failed to persist unexpected disconnect state for bot {BotId}", botId);
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength);
        }

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
            _client.SlashCommandExecuted -= HandleSlashCommandExecutedAsync;
        }

        private async Task HandleSlashCommandExecutedAsync(SocketSlashCommand command)
        {
            if (!_currentBotId.HasValue)
            {
                _logger.LogWarning("Received slash command {CommandName} but no current bot id is set", command.CommandName);
                await command.RespondAsync("Bot není správně inicializován (missing bot id).", ephemeral: true);
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
                    await command.RespondAsync("Bot nenalezen v databázi.", ephemeral: true);
                    return;
                }

                var pluginRegistryType = ResolveType("BotManager.Api.BotPlugins.PluginRegistry");
                var pluginContextType = ResolveType("BotManager.Api.BotPlugins.PluginContext");
                if (pluginRegistryType == null || pluginContextType == null)
                {
                    _logger.LogError("Plugin types were not resolved. PluginRegistry={RegistryFound}, PluginContext={ContextFound}",
                        pluginRegistryType != null,
                        pluginContextType != null);
                    await command.RespondAsync("Plugin infrastruktura není dostupná.", ephemeral: true);
                    return;
                }

                var pluginRegistry = serviceProvider.GetService(pluginRegistryType);
                if (pluginRegistry == null)
                {
                    _logger.LogError("PluginRegistry service is not registered in DI");
                    await command.RespondAsync("Plugin registry není dostupný.", ephemeral: true);
                    return;
                }

                var getOrCreatePluginMethod = pluginRegistryType.GetMethod("GetOrCreatePlugin", new[] { typeof(int), typeof(string) });
                if (getOrCreatePluginMethod == null)
                {
                    _logger.LogError("PluginRegistry.GetOrCreatePlugin method was not found");
                    await command.RespondAsync("Plugin registry je v neplatném stavu.", ephemeral: true);
                    return;
                }

                var plugin = getOrCreatePluginMethod.Invoke(pluginRegistry, new object[] { _currentBotId.Value, "discord-aliance" });
                if (plugin == null)
                {
                    _logger.LogError("Plugin instance creation failed for bot {BotId}", _currentBotId.Value);
                    await command.RespondAsync("Nepodařilo se načíst plugin.", ephemeral: true);
                    return;
                }

                var pluginContext = Activator.CreateInstance(pluginContextType);
                if (pluginContext == null)
                {
                    _logger.LogError("PluginContext instance creation failed");
                    await command.RespondAsync("Nepodařilo se vytvořit plugin context.", ephemeral: true);
                    return;
                }

                SetRequiredProperty(pluginContextType, pluginContext, "Bot", bot);
                SetRequiredProperty(pluginContextType, pluginContext, "DbContext", db);

                var systemConfigServiceType = ResolveType("BotManager.Api.Services.SystemConfigService");
                var teamsDataServiceType = ResolveType("BotManager.Api.Services.ITeamsDataService");
                var botDataServiceType = ResolveType("BotManager.Api.Services.IBotDataService");
                if (systemConfigServiceType == null || teamsDataServiceType == null || botDataServiceType == null)
                {
                    _logger.LogError("Failed to resolve plugin context service types. SystemConfig={SystemConfigFound}, Teams={TeamsFound}, BotData={BotDataFound}",
                        systemConfigServiceType != null,
                        teamsDataServiceType != null,
                        botDataServiceType != null);
                    await command.RespondAsync("Plugin služby nejsou dostupné.", ephemeral: true);
                    return;
                }

                var systemConfigService = serviceProvider.GetService(systemConfigServiceType);
                var teamsDataService = serviceProvider.GetService(teamsDataServiceType);
                var botDataService = serviceProvider.GetService(botDataServiceType);
                if (systemConfigService == null || teamsDataService == null || botDataService == null)
                {
                    _logger.LogError("Failed to resolve plugin context service instances. SystemConfig={SystemConfigFound}, Teams={TeamsFound}, BotData={BotDataFound}",
                        systemConfigService != null,
                        teamsDataService != null,
                        botDataService != null);
                    await command.RespondAsync("Plugin služby nejsou inicializované.", ephemeral: true);
                    return;
                }

                SetRequiredProperty(pluginContextType, pluginContext, "SystemConfigService", systemConfigService);
                SetRequiredProperty(pluginContextType, pluginContext, "TeamsDataService", teamsDataService);
                SetRequiredProperty(pluginContextType, pluginContext, "BotDataService", botDataService);
                SetRequiredProperty(pluginContextType, pluginContext, "Logger", _logger);
                SetRequiredProperty(pluginContextType, pluginContext, "ServiceProvider", serviceProvider);

                if (!_pluginInitialized)
                {
                    await InvokeTaskMethodAsync(plugin, "InitializeAsync", pluginContext);
                    _pluginInitialized = true;
                }

                var handled = await InvokeTaskBoolMethodAsync(plugin, "HandleCommandAsync", command, pluginContext);
                if (!handled)
                {
                    _logger.LogWarning("Command {CommandName} was not handled by plugin", command.CommandName);
                    if (!command.HasResponded)
                    {
                        await command.RespondAsync("Příkaz nebyl pluginem zpracován.", ephemeral: true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while dispatching slash command {CommandName} to plugin", command.CommandName);
                if (!command.HasResponded)
                {
                    await command.RespondAsync("Nastala chyba při zpracování příkazu.", ephemeral: true);
                }
            }
        }

        private static Type? ResolveType(string fullTypeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullTypeName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static void SetRequiredProperty(Type targetType, object instance, string propertyName, object value)
        {
            var property = targetType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite)
            {
                throw new InvalidOperationException($"Property '{propertyName}' was not found on type '{targetType.FullName}'");
            }

            property.SetValue(instance, value);
        }

        private static async Task InvokeTaskMethodAsync(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                throw new InvalidOperationException($"Method '{methodName}' was not found on type '{target.GetType().FullName}'");
            }

            if (method.Invoke(target, args) is not Task task)
            {
                throw new InvalidOperationException($"Method '{methodName}' did not return Task");
            }

            await task;
        }

        private static async Task<bool> InvokeTaskBoolMethodAsync(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                throw new InvalidOperationException($"Method '{methodName}' was not found on type '{target.GetType().FullName}'");
            }

            if (method.Invoke(target, args) is not Task task)
            {
                throw new InvalidOperationException($"Method '{methodName}' did not return Task");
            }

            await task;

            var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
            if (resultProperty == null)
            {
                throw new InvalidOperationException($"Method '{methodName}' did not return Task<T>");
            }

            var value = resultProperty.GetValue(task);
            return value is bool b && b;
        }
    }
}
