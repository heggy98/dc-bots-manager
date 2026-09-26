using System.Text.Json.Serialization;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Verifies Google reCAPTCHA (v2 checkbox) tokens. Enabled only when both
    /// Recaptcha:SiteKey and Recaptcha:SecretKey are configured; then it is required after
    /// Recaptcha:RequiredAfterFailedAttempts failed logins (default 3) from the same IP.
    /// </summary>
    public class RecaptchaService
    {
        private const string VerifyUrl = "https://www.google.com/recaptcha/api/siteverify";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<RecaptchaService> _logger;
        private readonly string? _secretKey;

        /// <summary>
        /// Creates a new reCAPTCHA verification service.
        /// </summary>
        public RecaptchaService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<RecaptchaService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _secretKey = configuration["Recaptcha:SecretKey"];
            SiteKey = configuration["Recaptcha:SiteKey"];
            RequiredAfterFailedAttempts = int.TryParse(configuration["Recaptcha:RequiredAfterFailedAttempts"], out var n) && n >= 0 ? n : 3;
        }

        /// <summary>Public site key for the frontend widget (null when disabled).</summary>
        public string? SiteKey { get; }

        /// <summary>Failed attempts after which the captcha is required.</summary>
        public int RequiredAfterFailedAttempts { get; }

        /// <summary>Whether reCAPTCHA is configured.</summary>
        public bool IsEnabled => !string.IsNullOrWhiteSpace(SiteKey) && !string.IsNullOrWhiteSpace(_secretKey);

        /// <summary>
        /// Returns whether a captcha is required for the given number of failed attempts.
        /// </summary>
        public bool IsRequired(int failedAttempts) => IsEnabled && failedAttempts >= RequiredAfterFailedAttempts;

        /// <summary>
        /// Verifies a widget token with Google. Returns false on any error.
        /// </summary>
        public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken = default)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            try
            {
                var form = new Dictionary<string, string> { ["secret"] = _secretKey!, ["response"] = token };
                if (!string.IsNullOrWhiteSpace(remoteIp))
                {
                    form["remoteip"] = remoteIp;
                }

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                using var response = await client.PostAsync(VerifyUrl, new FormUrlEncodedContent(form), cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("reCAPTCHA verification returned HTTP {StatusCode}", (int)response.StatusCode);
                    return false;
                }

                var result = await response.Content.ReadFromJsonAsync<VerifyResponse>(cancellationToken);
                return result?.Success == true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "reCAPTCHA verification failed");
                return false;
            }
        }

        private sealed class VerifyResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }
        }
    }
}
