using BotManager.Backend.Shared.Services;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Holds the "tokens issued before this instant are invalid" watermark used by "log out everywhere".
    /// The value is persisted in SystemConfigs (key <see cref="ConfigKey"/>) and cached in memory.
    /// </summary>
    public class TokenRevocationState
    {
        /// <summary>SystemConfig key storing the watermark as Unix seconds.</summary>
        public const string ConfigKey = "Auth.TokensValidAfter";

        private long _validAfterUnixSeconds;

        /// <summary>Current watermark in Unix seconds (0 = none).</summary>
        public long ValidAfterUnixSeconds => Interlocked.Read(ref _validAfterUnixSeconds);

        /// <summary>
        /// Loads the persisted watermark (called once at startup).
        /// </summary>
        public async Task LoadAsync(ISystemConfigService systemConfig)
        {
            var value = await systemConfig.GetValueAsync(ConfigKey);
            if (long.TryParse(value, out var seconds))
            {
                Interlocked.Exchange(ref _validAfterUnixSeconds, seconds);
            }
        }

        /// <summary>
        /// Invalidates all access tokens issued up to now and persists the watermark.
        /// </summary>
        public async Task RevokeAllIssuedUntilNowAsync(ISystemConfigService systemConfig)
        {
            var now = EpochTime.GetIntDate(DateTime.UtcNow);
            Interlocked.Exchange(ref _validAfterUnixSeconds, now);
            await systemConfig.SetValueAsync(ConfigKey, now.ToString());
        }

        /// <summary>
        /// Returns whether a validated principal's token was issued after the watermark.
        /// </summary>
        public bool IsTokenStillValid(ClaimsPrincipal principal)
        {
            var watermark = ValidAfterUnixSeconds;
            if (watermark == 0)
            {
                return true;
            }

            var iat = principal.FindFirst(JwtRegisteredClaimNames.Iat)?.Value;
            return long.TryParse(iat, out var issuedAt) && issuedAt > watermark;
        }
    }
}
