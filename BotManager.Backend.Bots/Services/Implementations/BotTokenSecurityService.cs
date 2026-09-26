using System.Security.Cryptography;
using System.Text;
using BotManager.Backend.Bots.Services.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Protects Discord bot tokens at rest and produces safe display values.
    /// </summary>
    public sealed class BotTokenSecurityService : IBotTokenSecurityService
    {
        private const string EnvelopePrefix = "v1";
        private readonly IDataProtector _protector;
        private readonly ILogger<BotTokenSecurityService> _logger;

        public BotTokenSecurityService(IDataProtectionProvider dataProtectionProvider,
            ILogger<BotTokenSecurityService>? logger = null)
        {
            _protector = dataProtectionProvider.CreateProtector("BotManager.Backend.Bots.BotToken");
            _logger = logger ?? NullLogger<BotTokenSecurityService>.Instance;
        }

        public string NormalizeRawToken(string rawToken)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return string.Empty;
            }

            var normalized = rawToken.Trim().Trim('"', '\'');
            if (normalized.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[4..].Trim();
            }

            return normalized;
        }

        public string ProtectForStorage(string rawToken)
        {
            var normalized = NormalizeRawToken(rawToken);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            var hashHex = ComputeSha256Hex(normalized);
            var protectedPayload = _protector.Protect(normalized);
            return $"{EnvelopePrefix}:{hashHex}:{protectedPayload}";
        }

        public bool TryGetRawToken(string storedToken, out string rawToken)
        {
            rawToken = string.Empty;
            if (string.IsNullOrWhiteSpace(storedToken))
            {
                return false;
            }

            if (!TryParseEnvelope(storedToken, out _, out var protectedPayload))
            {
                // Plain-text tokens are no longer accepted; they are re-protected at startup
                // (see TryProtectLegacyToken).
                _logger.LogWarning("Stored bot token is not in the protected envelope format and was rejected.");
                return false;
            }

            try
            {
                var unprotected = _protector.Unprotect(protectedPayload);
                var normalized = NormalizeRawToken(unprotected);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    return false;
                }

                rawToken = normalized;
                return true;
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex,
                    "Failed to unprotect stored bot token. The Data Protection key ring was probably lost or changed; re-enter the bot token.");
                return false;
            }
        }

        public bool IsProtected(string storedToken)
            => !string.IsNullOrWhiteSpace(storedToken) && TryParseEnvelope(storedToken, out _, out _);

        public bool TryProtectLegacyToken(string storedToken, out string protectedToken)
        {
            protectedToken = string.Empty;
            if (string.IsNullOrWhiteSpace(storedToken) || IsProtected(storedToken))
            {
                return false;
            }

            protectedToken = ProtectForStorage(storedToken);
            return !string.IsNullOrEmpty(protectedToken);
        }

        public string BuildMaskedToken(string storedToken)
        {
            if (!TryGetRawToken(storedToken, out var rawToken) || string.IsNullOrWhiteSpace(rawToken))
            {
                return "***";
            }

            var prefixLength = Math.Min(3, rawToken.Length);
            var prefix = rawToken.Substring(0, prefixLength);
            var stars = new string('*', Math.Max(6, rawToken.Length - prefixLength));
            return $"{prefix}{stars}";
        }

        private static string ComputeSha256Hex(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes);
        }

        private static bool TryParseEnvelope(string storedToken, out string hashHex, out string protectedPayload)
        {
            hashHex = string.Empty;
            protectedPayload = string.Empty;

            var firstColon = storedToken.IndexOf(':');
            if (firstColon <= 0)
            {
                return false;
            }

            var secondColon = storedToken.IndexOf(':', firstColon + 1);
            if (secondColon <= firstColon + 1)
            {
                return false;
            }

            var prefix = storedToken[..firstColon];
            if (!prefix.Equals(EnvelopePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            hashHex = storedToken[(firstColon + 1)..secondColon];
            protectedPayload = storedToken[(secondColon + 1)..];
            return !string.IsNullOrWhiteSpace(hashHex) && !string.IsNullOrWhiteSpace(protectedPayload);
        }
    }
}
