using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BotManager.Backend.Bots.Models;
using BotManager.Backend.Bots.Services.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Sends bot down/recovery alerts to a Discord webhook and/or via SMTP e-mail.
    /// At most one down alert (offline / start failed) per bot is sent within <see cref="BotAlertOptions.DebounceMinutes"/>;
    /// a recovery alert is only sent after a down alert was actually delivered, so a flapping bot cannot spam.
    /// </summary>
    public sealed partial class BotAlertService : IBotAlertService
    {
        /// <summary>Name of the <see cref="IHttpClientFactory"/> client used for the Discord webhook.</summary>
        public const string HttpClientName = "BotAlerts";

        private const int MaxReasonLength = 1000;
        private const int MaxNameLength = 100;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly BotAlertOptions _options;
        private readonly ILogger<BotAlertService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly Uri? _webhookUri;
        private readonly bool _smtpEnabled;
        private readonly ConcurrentDictionary<int, BotAlertState> _states = new();

        /// <summary>
        /// Creates a new alert service.
        /// </summary>
        public BotAlertService(
            IHttpClientFactory httpClientFactory,
            IOptions<BotAlertOptions> options,
            ILogger<BotAlertService> logger,
            TimeProvider? timeProvider = null)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;

            if (!string.IsNullOrWhiteSpace(_options.DiscordWebhookUrl))
            {
                if (Uri.TryCreate(_options.DiscordWebhookUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                {
                    _webhookUri = uri;
                }
                else
                {
                    // The URL contains the webhook secret: never log it.
                    _logger.LogWarning("Alerts:DiscordWebhookUrl is not an absolute https URL; Discord alerts are disabled");
                }
            }

            var smtp = _options.Smtp;
            _smtpEnabled = !string.IsNullOrWhiteSpace(smtp.Host)
                && !string.IsNullOrWhiteSpace(smtp.From)
                && !string.IsNullOrWhiteSpace(smtp.To);
            if (!string.IsNullOrWhiteSpace(smtp.Host) && !_smtpEnabled)
            {
                _logger.LogWarning("Alerts:Smtp:Host is set but From/To are missing; e-mail alerts are disabled");
            }
        }

        /// <inheritdoc />
        public bool IsEnabled => _webhookUri != null || _smtpEnabled;

        /// <inheritdoc />
        public async Task NotifyAsync(BotAlert alert, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!IsEnabled || !TryReserve(alert))
                {
                    return;
                }

                var sends = new List<Task>(2);
                if (_webhookUri != null)
                {
                    sends.Add(SendWebhookAsync(_webhookUri, alert, cancellationToken));
                }

                if (_smtpEnabled)
                {
                    sends.Add(SendEmailAsync(alert, cancellationToken));
                }

                await Task.WhenAll(sends);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send {Kind} alert for bot {BotId}", alert.Kind, alert.BotId);
            }
        }

        /// <summary>
        /// Applies the per-bot rate limit. Returns true when the alert should be sent.
        /// </summary>
        private bool TryReserve(BotAlert alert)
        {
            var state = _states.GetOrAdd(alert.BotId, _ => new BotAlertState());
            var now = _timeProvider.GetUtcNow();
            var window = TimeSpan.FromMinutes(Math.Max(0, _options.DebounceMinutes));

            lock (state)
            {
                if (alert.Kind == BotAlertKind.Recovered)
                {
                    if (!_options.NotifyRecovery || !state.DownAlertOutstanding)
                    {
                        return false;
                    }

                    state.DownAlertOutstanding = false;
                    return true;
                }

                if (state.LastDownAlertAt is { } last && now - last < window)
                {
                    _logger.LogInformation(
                        "Suppressed {Kind} alert for bot {BotId}: last alert was sent at {LastAlertAt:O}",
                        alert.Kind, alert.BotId, last);
                    return false;
                }

                state.LastDownAlertAt = now;
                state.DownAlertOutstanding = true;
                return true;
            }
        }

        /// <summary>
        /// Posts the alert as an embed to the Discord webhook. Mentions are disabled.
        /// </summary>
        private async Task SendWebhookAsync(Uri webhookUri, BotAlert alert, CancellationToken cancellationToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.PostAsJsonAsync(webhookUri, BuildWebhookPayload(alert, _timeProvider.GetUtcNow()), JsonOptions, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Discord alert webhook returned {StatusCode} for bot {BotId}", (int)response.StatusCode, alert.BotId);
                }
            }
            catch (Exception ex)
            {
                // Log the exception type/message only: HttpRequestException messages never contain the URL path,
                // but the webhook URL itself (it embeds the secret) is never logged.
                _logger.LogWarning("Failed to post Discord alert for bot {BotId}: {Error}", alert.BotId, ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Sends the alert as a plain-text e-mail via SMTP.
        /// </summary>
        private async Task SendEmailAsync(BotAlert alert, CancellationToken cancellationToken)
        {
            var smtp = _options.Smtp;
            try
            {
                using var client = new SmtpClient(smtp.Host, smtp.Port)
                {
                    EnableSsl = smtp.UseSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 15000
                };
                if (!string.IsNullOrEmpty(smtp.User))
                {
                    client.Credentials = new NetworkCredential(smtp.User, smtp.Password);
                }

                using var message = new MailMessage
                {
                    From = new MailAddress(smtp.From!),
                    Subject = BuildTitle(alert),
                    Body = BuildText(alert, _timeProvider.GetUtcNow()),
                    IsBodyHtml = false,
                    BodyEncoding = Encoding.UTF8,
                    SubjectEncoding = Encoding.UTF8
                };
                foreach (var to in smtp.To!.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    message.To.Add(to);
                }

                await client.SendMailAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to send alert e-mail for bot {BotId}: {Error}", alert.BotId, ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Builds the Discord webhook JSON payload (one embed, no mentions).
        /// </summary>
        internal static object BuildWebhookPayload(BotAlert alert, DateTimeOffset timestamp)
        {
            var reason = SanitizeReason(alert.Reason);
            return new
            {
                username = "BotManager",
                allowed_mentions = new { parse = Array.Empty<string>() },
                embeds = new[]
                {
                    new
                    {
                        title = BuildTitle(alert),
                        description = string.IsNullOrEmpty(reason) ? null : reason,
                        color = alert.Kind == BotAlertKind.Recovered ? 0x2ECC71 : 0xE74C3C,
                        timestamp = timestamp.ToString("O"),
                        fields = new[]
                        {
                            new { name = "Bot ID", value = alert.BotId.ToString(System.Globalization.CultureInfo.InvariantCulture), inline = true },
                            new { name = "Event", value = alert.Kind.ToString(), inline = true }
                        }
                    }
                }
            };
        }

        /// <summary>
        /// Builds the alert title (also used as e-mail subject).
        /// </summary>
        internal static string BuildTitle(BotAlert alert)
        {
            var name = SanitizeName(alert.BotName);
            return alert.Kind switch
            {
                BotAlertKind.Offline => $"Bot \"{name}\" is offline",
                BotAlertKind.StartFailed => $"Bot \"{name}\" failed to start",
                BotAlertKind.Recovered => $"Bot \"{name}\" is back online",
                _ => $"Bot \"{name}\": {alert.Kind}"
            };
        }

        /// <summary>
        /// Builds the plain-text e-mail body.
        /// </summary>
        private static string BuildText(BotAlert alert, DateTimeOffset timestamp)
        {
            var builder = new StringBuilder()
                .AppendLine(BuildTitle(alert))
                .AppendLine()
                .AppendLine($"Bot ID: {alert.BotId}")
                .AppendLine($"Event: {alert.Kind}")
                .AppendLine($"Time (UTC): {timestamp.UtcDateTime:yyyy-MM-dd HH:mm:ss}");
            var reason = SanitizeReason(alert.Reason);
            if (!string.IsNullOrEmpty(reason))
            {
                builder.AppendLine($"Reason: {reason}");
            }

            return builder.ToString();
        }

        /// <summary>
        /// Strips control characters (no header injection) and limits the length of a bot name.
        /// </summary>
        private static string SanitizeName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "(unnamed)";
            }

            var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
            return cleaned.Length > MaxNameLength ? cleaned[..MaxNameLength] : cleaned;
        }

        /// <summary>
        /// Redacts anything that looks like a Discord bot token or webhook URL and limits the length.
        /// </summary>
        internal static string? SanitizeReason(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return null;
            }

            var redacted = WebhookUrlPattern().Replace(reason, "[redacted-webhook]");
            redacted = BotTokenPattern().Replace(redacted, "[redacted-token]");
            return redacted.Length > MaxReasonLength ? redacted[..MaxReasonLength] + "…" : redacted;
        }

        [GeneratedRegex(@"[A-Za-z0-9_\-]{20,}\.[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{20,}")]
        private static partial Regex BotTokenPattern();

        [GeneratedRegex(@"https?://\S*/webhooks/\S+", RegexOptions.IgnoreCase)]
        private static partial Regex WebhookUrlPattern();

        /// <summary>
        /// Rate-limit state of one bot.
        /// </summary>
        private sealed class BotAlertState
        {
            public DateTimeOffset? LastDownAlertAt { get; set; }
            public bool DownAlertOutstanding { get; set; }
        }
    }

    /// <summary>
    /// DI registration of the bot alert service.
    /// </summary>
    public static class BotAlertServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="IBotAlertService"/> bound to the <c>Alerts</c> configuration section.
        /// </summary>
        public static IServiceCollection AddBotAlerts(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<BotAlertOptions>(configuration.GetSection(BotAlertOptions.SectionName));
            // No request logging for this client: the webhook URL embeds its secret.
            services.AddHttpClient(BotAlertService.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
                .RemoveAllLoggers();
            services.AddSingleton<BotAlertService>();
            services.AddSingleton<IBotAlertService>(sp => sp.GetRequiredService<BotAlertService>());
            return services;
        }
    }
}
