using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Services
{
    public class AuthService
    {
        private readonly BotManagerDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthService> _logger;

        public AuthService(BotManagerDbContext db, IConfiguration config, ILogger<AuthService> logger)
        {
            _db = db;
            _config = config;
            _logger = logger;
        }

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

        public async Task<GoogleJsonWebSignature.Payload?> VerifyGoogleTokenAsync(string idToken)
        {
            try
            {
                var clientId = _config["GoogleAuth:ClientId"];
                var settings = new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { clientId }
                };
                var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
                return payload;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Google token validation failed: {Message}", ex.Message);
                return null;
            }
        }
    }
}
