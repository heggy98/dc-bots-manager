namespace BotManager.Backend.Bots.Models
{
    /// <summary>
    /// Configuration of bot down/recovery alerts (section <c>Alerts</c>). Every channel is optional.
    /// </summary>
    public sealed class BotAlertOptions
    {
        /// <summary>Configuration section name.</summary>
        public const string SectionName = "Alerts";

        /// <summary>Discord webhook URL (https) alerts are posted to; empty disables the channel.</summary>
        public string? DiscordWebhookUrl { get; set; }

        /// <summary>SMTP e-mail channel; disabled while <see cref="SmtpAlertOptions.Host"/> is empty.</summary>
        public SmtpAlertOptions Smtp { get; set; } = new();

        /// <summary>Minimum minutes between two down alerts (offline / start failed) of the same bot.</summary>
        public int DebounceMinutes { get; set; } = 10;

        /// <summary>Sends a "back online" alert after a bot recovers from an alerted outage.</summary>
        public bool NotifyRecovery { get; set; } = true;
    }

    /// <summary>
    /// SMTP settings of the e-mail alert channel.
    /// </summary>
    public sealed class SmtpAlertOptions
    {
        /// <summary>SMTP server host name; empty disables e-mail alerts.</summary>
        public string? Host { get; set; }

        /// <summary>SMTP port (default 587, submission with STARTTLS).</summary>
        public int Port { get; set; } = 587;

        /// <summary>Optional user name for SMTP authentication.</summary>
        public string? User { get; set; }

        /// <summary>Optional password for SMTP authentication.</summary>
        public string? Password { get; set; }

        /// <summary>Sender address.</summary>
        public string? From { get; set; }

        /// <summary>Recipient address(es), separated by comma or semicolon.</summary>
        public string? To { get; set; }

        /// <summary>Uses STARTTLS (default true). Implicit TLS on port 465 is not supported.</summary>
        public bool UseSsl { get; set; } = true;
    }
}
