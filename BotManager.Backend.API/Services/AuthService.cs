using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Provides authentication helper operations such as login auditing and Google token validation.
    /// </summary>
    public class AuthService
    {
        private readonly BotManagerDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthService> _logger;

        /// <summary>
        /// Creates a new authentication service instance.
        /// </summary>
        public AuthService(BotManagerDbContext db, IConfiguration config, ILogger<AuthService> logger)
        {
            _db = db;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Persists a login attempt to the audit log.
        /// </summary>
        public async Task LogLoginAttemptAsync(string email, string ip, bool success, string? failReason = null, bool isBruteforce = false)
        {
            _db.LoginAuditLogs.Add(new LoginAuditLog
            {
                Email = email,
                IpAddress = ip,
                Success = success,
                FailReason = failReason,
                IsBruteforceBlock = isBruteforce,
                Timestamp = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Validates a Google ID token against the configured Google OAuth client id.
        /// </summary>
        public async Task<GoogleJsonWebSignature.Payload?> VerifyGoogleTokenAsync(string idToken)
        {
            var clientId = _config["GoogleAuth:ClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
            {
                _logger.LogWarning("Google token validation skipped: GoogleAuth:ClientId is not configured.");
                return null;
            }

            try
            {
                var settings = new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { clientId }
                };

                return await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Google token validation failed: {Message}", ex.Message);
                return null;
            }
        }
    }
}
