using Discord;
using Discord.WebSocket;
using BotManager.Backend.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BotManager.Backend.Services.Implementation
{
    public class DiscordBotAllianceService : IDiscordBotService
    {
        private DiscordSocketClient _client;
        private readonly ILogger<DiscordBotAllianceService> _logger;
        private bool _isRunning = false;
        private CancellationTokenSource _cancellationTokenSource;

        public DiscordBotAllianceService(ILogger<DiscordBotAllianceService> logger)
        {
            _logger = logger;
        }

        public async Task StartAsync(string botToken)
        {
            if (_isRunning)
            {
                _logger.LogWarning("Bot is already running");
                return;
            }

            try
            {
                _cancellationTokenSource = new CancellationTokenSource();

                var config = new DiscordSocketConfig
                {
                    GatewayIntents =
                        GatewayIntents.Guilds |
                        GatewayIntents.GuildMessages |
                        GatewayIntents.MessageContent |
                        GatewayIntents.GuildMessageReactions |
                        GatewayIntents.GuildMembers
                };

                _client = new DiscordSocketClient(config);

                _client.Log += LogAsync;
                _client.Ready += ReadyAsync;

                await _client.LoginAsync(TokenType.Bot, botToken);
                await _client.StartAsync();

                _isRunning = true;
                _logger.LogInformation("Discord bot alliance started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Discord bot alliance");
                _isRunning = false;
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
                _cancellationTokenSource?.Cancel();
                await _client?.StopAsync();
                await _client?.LogoutAsync();
                _isRunning = false;
                _logger.LogInformation("Discord bot alliance stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping Discord bot alliance");
                throw;
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

            return _client?.ConnectionState switch
            {
                ConnectionState.Connected => "Online",
                ConnectionState.Connecting => "Connecting",
                ConnectionState.Disconnecting => "Disconnecting",
                ConnectionState.Disconnected => "Offline",
                _ => "Unknown"
            };
        }

        private async Task ReadyAsync()
        {
            _logger.LogInformation("Discord bot alliance is ready");
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

            _logger.Log(severity, "{Source}: {Message}", msg.Source, msg.Message);
            await Task.CompletedTask;
        }
    }
}
