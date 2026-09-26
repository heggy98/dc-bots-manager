using BotManager.Backend.API.Models;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Issues and rotates login sessions: a short-lived JWT access token plus an opaque refresh token,
    /// both delivered only as HttpOnly cookies.
    /// </summary>
    public class SessionService
    {
        /// <summary>Cookie holding the JWT access token.</summary>
        public const string AccessCookieName = "bm_access";

        /// <summary>Cookie holding the opaque refresh token (scoped to the auth endpoints).</summary>
        public const string RefreshCookieName = "bm_refresh";

        /// <summary>Path of the refresh cookie; it is only sent to the auth endpoints.</summary>
        public const string RefreshCookiePath = "/api/auth";

        private const int DefaultRefreshTokenDays = 14;

        private readonly BotManagerDbContext _db;
        private readonly JwtTokenService _jwtTokenService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SessionService> _logger;

        /// <summary>
        /// Creates a new session service.
        /// </summary>
        public SessionService(BotManagerDbContext db, JwtTokenService jwtTokenService, IConfiguration configuration,
            ILogger<SessionService> logger)
        {
            _db = db;
            _jwtTokenService = jwtTokenService;
            _configuration = configuration;
            _logger = logger;
        }

        private TimeSpan RefreshLifetime => TimeSpan.FromDays(
            int.TryParse(_configuration["Auth:RefreshTokenDays"], out var days) && days > 0 ? days : DefaultRefreshTokenDays);

        private bool SecureCookies => !string.Equals(_configuration["Auth:CookieSecure"], "false", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Creates a new session for an authenticated user and writes the auth cookies.
        /// </summary>
        public async Task<LoginResponse> CreateSessionAsync(HttpResponse response, string email, string ip)
        {
            var (rawRefreshToken, refreshEntity) = NewRefreshToken(email, ip);
            _db.RefreshTokens.Add(refreshEntity);
            await _db.SaveChangesAsync();

            return IssueCookies(response, email, rawRefreshToken, refreshEntity.ExpiresAt);
        }

        /// <summary>
        /// Rotates the refresh token from the request cookie. Returns null when the token is missing,
        /// expired or revoked. Presenting an already rotated token revokes all sessions of the user.
        /// </summary>
        public async Task<LoginResponse?> RefreshAsync(HttpRequest request, HttpResponse response, string ip)
        {
            if (!request.Cookies.TryGetValue(RefreshCookieName, out var rawToken) || string.IsNullOrWhiteSpace(rawToken))
            {
                return null;
            }

            var hash = Hash(rawToken);
            var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
            if (stored == null)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            if (stored.RevokedAt != null)
            {
                // Reuse of a rotated/revoked token: the token was probably stolen. Kill every session.
                _logger.LogWarning("Refresh token reuse detected for {Email} from {Ip}; revoking all sessions", stored.Email, ip);
                await RevokeAllAsync(stored.Email);
                return null;
            }

            if (stored.ExpiresAt <= now)
            {
                return null;
            }

            var (newRawToken, newEntity) = NewRefreshToken(stored.Email, ip);
            stored.RevokedAt = now;
            stored.ReplacedByTokenHash = newEntity.TokenHash;
            _db.RefreshTokens.Add(newEntity);
            await _db.SaveChangesAsync();

            return IssueCookies(response, stored.Email, newRawToken, newEntity.ExpiresAt);
        }

        /// <summary>
        /// Revokes the refresh token from the request cookie (if any) and clears the auth cookies.
        /// </summary>
        public async Task LogoutAsync(HttpRequest request, HttpResponse response)
        {
            if (request.Cookies.TryGetValue(RefreshCookieName, out var rawToken) && !string.IsNullOrWhiteSpace(rawToken))
            {
                var hash = Hash(rawToken);
                await _db.RefreshTokens
                    .Where(t => t.TokenHash == hash && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
            }

            ClearCookies(response);
        }

        /// <summary>
        /// Revokes every active refresh token of a user.
        /// </summary>
        public async Task<int> RevokeAllAsync(string email)
        {
            var now = DateTime.UtcNow;
            return await _db.RefreshTokens
                .Where(t => t.Email == email && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
        }

        /// <summary>
        /// Deletes both auth cookies.
        /// </summary>
        public void ClearCookies(HttpResponse response)
        {
            response.Cookies.Delete(AccessCookieName, CookieOptions(DateTime.UnixEpoch, "/"));
            response.Cookies.Delete(RefreshCookieName, CookieOptions(DateTime.UnixEpoch, RefreshCookiePath));
        }

        /// <summary>
        /// Hashes a raw refresh token for storage/lookup.
        /// </summary>
        public static string Hash(string rawToken)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        private LoginResponse IssueCookies(HttpResponse response, string email, string rawRefreshToken, DateTime refreshExpiresAt)
        {
            var accessToken = _jwtTokenService.GenerateToken(email, out var accessExpiresAt);

            response.Cookies.Append(AccessCookieName, accessToken, CookieOptions(accessExpiresAt, "/"));
            response.Cookies.Append(RefreshCookieName, rawRefreshToken, CookieOptions(refreshExpiresAt, RefreshCookiePath));

            return new LoginResponse
            {
                Email = email,
                AccessTokenExpiresAt = accessExpiresAt,
                RefreshTokenExpiresAt = refreshExpiresAt
            };
        }

        private (string RawToken, RefreshToken Entity) NewRefreshToken(string email, string ip)
        {
            var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
            var now = DateTime.UtcNow;
            return (raw, new RefreshToken
            {
                TokenHash = Hash(raw),
                Email = email,
                CreatedAt = now,
                ExpiresAt = now.Add(RefreshLifetime),
                CreatedByIp = ip.Length > 50 ? ip[..50] : ip
            });
        }

        private CookieOptions CookieOptions(DateTime expiresUtc, string path) => new()
        {
            HttpOnly = true,
            Secure = SecureCookies,
            SameSite = SameSiteMode.Strict,
            Path = path,
            Expires = new DateTimeOffset(expiresUtc, TimeSpan.Zero),
            IsEssential = true
        };

        private static string Base64UrlEncode(byte[] bytes)
            => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
