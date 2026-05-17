using BotManager.Backend.API.Hubs;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Entities.Entities;
using Microsoft.AspNetCore.SignalR;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Delivers real-time bot events to SignalR clients.
    /// </summary>
    public class BotNotificationService : IBotNotificationService
    {
        private readonly IHubContext<BotEventsHub> _hubContext;
        private readonly ILogger<BotNotificationService> _logger;

        public BotNotificationService(IHubContext<BotEventsHub> hubContext, ILogger<BotNotificationService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task NotifyBotStatusChangedAsync(int botId, BotStatus status)
        {
            try
            {
                await _hubContext.Clients
                    .Group(BotEventsHub.GroupName(botId))
                    .SendAsync("BotStatusChanged", new
                    {
                        botId,
                        status = (int)status,
                        statusText = status.ToString(),
                        timestamp = DateTime.UtcNow
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push BotStatusChanged for bot {BotId}", botId);
            }
        }

        public async Task NotifyNewLogAsync(int botId, string level, string message, DateTime timestamp)
        {
            try
            {
                await _hubContext.Clients
                    .Group(BotEventsHub.GroupName(botId))
                    .SendAsync("NewBotLog", new
                    {
                        botId,
                        timestamp,
                        level,
                        message
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push NewBotLog for bot {BotId}", botId);
            }
        }

        public async Task NotifyStatsUpdatedAsync(int botId, int requests24h, int errors24h)
        {
            try
            {
                await _hubContext.Clients
                    .Group(BotEventsHub.GroupName(botId))
                    .SendAsync("BotStatsUpdated", new
                    {
                        botId,
                        requests24h,
                        errors24h
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push BotStatsUpdated for bot {BotId}", botId);
            }
        }

        public async Task NotifyHistoryUpdatedAsync(int botId)
        {
            try
            {
                await _hubContext.Clients
                    .Group(BotEventsHub.GroupName(botId))
                    .SendAsync("BotHistoryUpdated", new { botId });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push BotHistoryUpdated for bot {BotId}", botId);
            }
        }
    }
}
