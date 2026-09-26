namespace BotManager.Backend.Bots.Services.Contracts
{
    /// <summary>
    /// Kind of an operator alert about a bot.
    /// </summary>
    public enum BotAlertKind
    {
        /// <summary>The bot was marked Offline after the gateway disconnect grace period expired.</summary>
        Offline,

        /// <summary>Starting the bot failed.</summary>
        StartFailed,

        /// <summary>The bot is back online after a previously alerted outage.</summary>
        Recovered
    }

    /// <summary>
    /// An operator alert about a bot. <paramref name="Reason"/> must never contain tokens or other secrets.
    /// </summary>
    public sealed record BotAlert(int BotId, string BotName, BotAlertKind Kind, string? Reason = null);

    /// <summary>
    /// Notifies operators (Discord webhook and/or e-mail) when a bot goes down or recovers.
    /// Alerts are rate-limited per bot; delivery failures are only logged, never thrown.
    /// </summary>
    public interface IBotAlertService
    {
        /// <summary>Whether at least one alert channel is configured.</summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Sends the alert on all configured channels unless it is suppressed by the per-bot rate limit.
        /// Never throws.
        /// </summary>
        Task NotifyAsync(BotAlert alert, CancellationToken cancellationToken = default);
    }
}
