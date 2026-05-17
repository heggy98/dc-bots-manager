using BotManager.Backend.Entities.Entities;

namespace BotManager.Backend.Bots.Services.Contracts
{
    /// <summary>
    /// Pushes real-time events to connected frontend clients via SignalR.
    /// </summary>
    public interface IBotNotificationService
    {
        /// <summary>Notifies clients that the bot status changed.</summary>
        Task NotifyBotStatusChangedAsync(int botId, BotStatus status);

        /// <summary>Notifies clients that a new command-usage log was written.</summary>
        Task NotifyNewLogAsync(int botId, string level, string message, DateTime timestamp);

        /// <summary>Notifies clients with the latest 24-hour request and error counts.</summary>
        Task NotifyStatsUpdatedAsync(int botId, int requests24h, int errors24h);

        /// <summary>Notifies clients that the bot run-history has changed and should be refreshed.</summary>
        Task NotifyHistoryUpdatedAsync(int botId);
    }
}
