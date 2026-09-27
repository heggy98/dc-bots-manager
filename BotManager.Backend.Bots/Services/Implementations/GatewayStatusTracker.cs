using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Discord;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Persists gateway connection state changes of a single bot (Reconnecting / Offline / Online)
    /// and notifies dashboard clients about them.
    /// </summary>
    internal sealed class GatewayStatusTracker
    {
        /// <summary>
        /// Seconds an unexpected disconnect may last before the bot is persisted as Offline.
        /// </summary>
        internal const int DisconnectGraceSeconds = 30;

        private readonly int _botId;
        private readonly ILogger _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IBotNotificationService _notificationService;
        private readonly IBotAlertService _alertService;
        private readonly TimeSpan _disconnectGrace;

        /// <summary>
        /// Creates a new gateway status tracker.
        /// </summary>
        public GatewayStatusTracker(
            int botId,
            ILogger logger,
            IServiceScopeFactory scopeFactory,
            IBotNotificationService notificationService,
            IBotAlertService alertService,
            TimeSpan? disconnectGrace = null)
        {
            _disconnectGrace = disconnectGrace ?? TimeSpan.FromSeconds(DisconnectGraceSeconds);
            _botId = botId;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _notificationService = notificationService;
            _alertService = alertService;
        }

        /// <summary>
        /// Persists the Reconnecting status and notifies clients (used for gateway-initiated reconnects).
        /// </summary>
        public async Task MarkReconnectingAsync()
        {
            await SetBotStatusInDbAsync(BotStatus.Reconnecting);
            await _notificationService.NotifyBotStatusChangedAsync(_botId, BotStatus.Reconnecting);
        }

        /// <summary>
        /// Marks a bot offline in storage after a disconnect grace period expires, unless the connection
        /// recovered or a newer connection event superseded this disconnect.
        /// </summary>
        /// <param name="disconnectGeneration">Connection generation captured when the disconnect happened.</param>
        /// <param name="getCurrentGeneration">Returns the session's current connection generation.</param>
        /// <param name="getConnectionState">Returns the session's current gateway connection state.</param>
        /// <param name="onMarkedNotRunning">Invoked when the session must be considered not running anymore.</param>
        public async Task MarkBotOfflineAfterGracePeriodAsync(
            Exception? ex,
            int disconnectGeneration,
            bool wasGatewayReconnect,
            Func<int> getCurrentGeneration,
            Func<ConnectionState> getConnectionState,
            Action onMarkedNotRunning)
        {
            await Task.Delay(_disconnectGrace);

            if (!GatewayDisconnectPolicy.ShouldMarkOffline(disconnectGeneration, getCurrentGeneration(), getConnectionState(), persistedStatus: null))
            {
                _logger.LogInformation("Skipping offline transition for bot {BotId}: reconnect state is already healthy or superseded.", _botId);
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();

                var bot = await db.Bots.FindAsync(_botId);
                if (bot == null)
                {
                    _logger.LogWarning("Could not persist unexpected disconnect state because bot {BotId} was not found", _botId);
                    onMarkedNotRunning();
                    return;
                }

                if (!GatewayDisconnectPolicy.ShouldMarkOffline(disconnectGeneration, getCurrentGeneration(), getConnectionState(), bot.Status))
                {
                    _logger.LogInformation("Bot {BotId} is already back Online — skipping offline transition.", _botId);
                    return;
                }

                var openHistory = await db.BotRunHistories
                    .Where(h => h.BotId == _botId && h.StoppedAt == null)
                    .OrderByDescending(h => h.StartedAt)
                    .FirstOrDefaultAsync();

                var stopAt = DateTime.UtcNow;
                var graceSeconds = _disconnectGrace.TotalSeconds;
                var reason = wasGatewayReconnect
                    ? $"Gateway reconnect failed (>{graceSeconds}s timeout)"
                    : $"Neočekávaný gateway disconnect (>{graceSeconds}s)";
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
                onMarkedNotRunning();

                _logger.LogWarning("Persisted unexpected disconnect to DB for bot {BotId}. Reason={Reason}", _botId, reason);
                await _notificationService.NotifyBotStatusChangedAsync(_botId, BotStatus.Offline);
                await _notificationService.NotifyHistoryUpdatedAsync(_botId);
                await _alertService.NotifyAsync(new BotAlert(
                    _botId,
                    bot.Name,
                    BotAlertKind.Offline,
                    ex == null ? reason : $"{reason}: {ex.GetType().Name}: {ex.Message}"));
            }
            catch (Exception persistEx)
            {
                _logger.LogError(persistEx, "Failed to persist unexpected disconnect state for bot {BotId}", _botId);
            }
        }

        /// <summary>
        /// Restores bot status to Online in DB when it reconnected after an unexpected disconnect.
        /// Creates a new run-history entry if needed.
        /// </summary>
        public async Task RestoreOnlineStatusIfNeededAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();

                var bot = await db.Bots.FindAsync(_botId);
                if (bot == null) return;

                if (bot.Status is not (BotStatus.Offline or BotStatus.Reconnecting)) return;

                var wasOffline = bot.Status == BotStatus.Offline;
                bot.Status = BotStatus.Online;
                bot.LastStoppedAt = null;

                // After a short Reconnecting phase the run is still open; only an Offline transition
                // closed it, so open a fresh history entry just in that case (avoids two open rows).
                var hasOpenHistory = await db.BotRunHistories.AnyAsync(h => h.BotId == _botId && h.StoppedAt == null);
                if (wasOffline || !hasOpenHistory)
                {
                    bot.LastStartedAt = DateTime.UtcNow;
                    if (!hasOpenHistory)
                    {
                        db.BotRunHistories.Add(new BotRunHistory { BotId = _botId, StartedAt = DateTime.UtcNow });
                    }
                }

                await db.SaveChangesAsync();

                _logger.LogInformation("Restored bot {BotId} to Online after reconnect.", _botId);
                await _notificationService.NotifyBotStatusChangedAsync(_botId, BotStatus.Online);
                await _notificationService.NotifyHistoryUpdatedAsync(_botId);
                await _alertService.NotifyAsync(new BotAlert(_botId, bot.Name, BotAlertKind.Recovered, "Gateway reconnected."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore Online status for bot {BotId} after reconnect", _botId);
            }
        }

        /// <summary>
        /// Updates only the bot's Status in DB without touching history or other fields.
        /// </summary>
        private async Task SetBotStatusInDbAsync(BotStatus status)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();

                var bot = await db.Bots.FindAsync(_botId);
                if (bot == null) return;

                bot.Status = status;
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set bot {BotId} status to {Status}", _botId, status);
            }
        }

        /// <summary>
        /// Truncates long values to a target maximum length.
        /// </summary>
        internal static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength);
        }
    }
}
