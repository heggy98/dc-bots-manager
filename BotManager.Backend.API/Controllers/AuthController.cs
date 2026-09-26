using BotManager.Backend.API.Models;
using BotManager.Backend.API.Services;
using BotManager.Backend.Services.Interfaces;
using BotManager.Backend.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace BotManager.Backend.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IBruteforceProtectionService _bruteforceProtection;
        private readonly AuthService _authService;
        private readonly AdminCredentialsService _adminCredentials;
        private readonly SessionService _sessionService;
        private readonly TokenRevocationState _tokenRevocation;
        private readonly RecaptchaService _recaptcha;
        private readonly ISystemConfigService _systemConfig;
        private readonly ILogger<AuthController> _logger;

        /// <summary>
        /// Creates a new authentication controller.
        /// </summary>
        public AuthController(IBruteforceProtectionService bruteforceProtection, AuthService authService,
            AdminCredentialsService adminCredentials, SessionService sessionService,
            TokenRevocationState tokenRevocation, RecaptchaService recaptcha,
            ISystemConfigService systemConfig, ILogger<AuthController> logger)
        {
            _bruteforceProtection = bruteforceProtection;
            _authService = authService;
            _adminCredentials = adminCredentials;
            _sessionService = sessionService;
            _tokenRevocation = tokenRevocation;
            _recaptcha = recaptcha;
            _systemConfig = systemConfig;
            _logger = logger;
        }

        /// <summary>Returns the current failed login attempts for the caller's IP and whether a captcha is required.</summary>
        [HttpGet("attempt-status")]
        [EnableRateLimiting(RateLimitPolicies.Public)]
        public IActionResult GetAttemptStatus()
        {
            var ip = GetIp();
            var locked = _bruteforceProtection.IsLocked(ip);
            var attempts = _bruteforceProtection.GetFailedAttempts(ip);
            return Ok(new
            {
                locked,
                attempts,
                captchaRequired = _recaptcha.IsRequired(attempts),
                captchaSiteKey = _recaptcha.IsEnabled ? _recaptcha.SiteKey : null
            });
        }

        /// <summary>
        /// Authenticates admin credentials using configured email/password hash.
        /// On success the session is delivered as HttpOnly cookies.
        /// </summary>
        [HttpPost("login")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var ip = GetIp();
            var accountKey = GetAccountKey(request.Email);

            if (_bruteforceProtection.IsLocked(ip) || _bruteforceProtection.IsLocked(accountKey))
            {
                await _authService.LogLoginAttemptAsync(request.Email ?? "", ip, false,
                    "Bruteforce lockout", isBruteforce: true);
                _logger.LogWarning("Login blocked for IP {Ip} (bruteforce)", ip);
                return StatusCode(429, "Too many login attempts. Please try again later.");
            }

            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return BadRequest("Email and password are required.");
            }

            if (_recaptcha.IsRequired(_bruteforceProtection.GetFailedAttempts(ip))
                && !await _recaptcha.VerifyAsync(request.RecaptchaToken, ip, HttpContext.RequestAborted))
            {
                await _authService.LogLoginAttemptAsync(request.Email, ip, false, "Chybějící nebo neplatná captcha");
                return BadRequest(new { message = "Captcha verification required.", captchaRequired = true });
            }

            if (_adminCredentials.Verify(request.Email, request.Password))
            {
                var email = _adminCredentials.AdminEmail;
                _bruteforceProtection.RegisterSuccess(ip);
                _bruteforceProtection.RegisterSuccess(accountKey);
                await _authService.LogLoginAttemptAsync(email, ip, true);
                _logger.LogInformation("User {Email} logged in successfully from {Ip}", email, ip);
                return Ok(await _sessionService.CreateSessionAsync(Response, email, ip));
            }

            await RegisterFailureAsync(ip, accountKey);
            await _authService.LogLoginAttemptAsync(request.Email, ip, false, "Nesprávné přihlašovací údaje");
            _logger.LogWarning("Failed login attempt for {Email} from {Ip}", request.Email, ip);
            return Unauthorized("Invalid credentials.");
        }

        /// <summary>
        /// Authenticates the configured admin account using Google ID token validation.
        /// </summary>
        [HttpPost("google-login")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> GoogleLogin([FromBody] LoginRequest request)
        {
            var ip = GetIp();

            if (_bruteforceProtection.IsLocked(ip))
            {
                await _authService.LogLoginAttemptAsync(request.Email ?? "", ip, false,
                    "Bruteforce lockout", isBruteforce: true);
                return StatusCode(429, "Too many login attempts. Please try again later.");
            }

            if (string.IsNullOrEmpty(request.GoogleIdToken))
            {
                return BadRequest("Google ID token is required.");
            }

            var payload = await _authService.VerifyGoogleTokenAsync(request.GoogleIdToken);
            if (payload == null)
            {
                await RegisterFailureAsync(ip, null);
                await _authService.LogLoginAttemptAsync(request.Email ?? "", ip, false, "Neplatný Google token");
                return Unauthorized("Invalid Google token.");
            }

            if (!payload.EmailVerified || !_adminCredentials.IsAdminEmail(payload.Email))
            {
                await RegisterFailureAsync(ip, null);
                await _authService.LogLoginAttemptAsync(payload.Email ?? "", ip, false, "Neautorizovaný Google účet");
                _logger.LogWarning("Unauthorized Google account login attempt: {Email}", payload.Email);
                return Unauthorized("Unauthorized Google account.");
            }

            var email = _adminCredentials.AdminEmail;
            _bruteforceProtection.RegisterSuccess(ip);
            await _authService.LogLoginAttemptAsync(email, ip, true);
            _logger.LogInformation("User {Email} logged in via Google from {Ip}", email, ip);
            return Ok(await _sessionService.CreateSessionAsync(Response, email, ip));
        }

        /// <summary>
        /// Rotates the refresh token cookie and issues a new access token cookie.
        /// </summary>
        [HttpPost("refresh")]
        [EnableRateLimiting(RateLimitPolicies.Public)]
        public async Task<IActionResult> Refresh()
        {
            var session = await _sessionService.RefreshAsync(Request, Response, GetIp());
            if (session == null)
            {
                _sessionService.ClearCookies(Response);
                return Unauthorized();
            }

            return Ok(session);
        }

        /// <summary>
        /// Returns the current session (used by the SPA to restore its state after a reload).
        /// </summary>
        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            var email = User.FindFirstValue(ClaimTypes.Email)
                ?? User.FindFirstValue(JwtRegisteredClaimNames.Email) ?? string.Empty;
            DateTime? expiresAt = long.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Exp), out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
                : null;
            return Ok(new { email, accessTokenExpiresAt = expiresAt });
        }

        /// <summary>
        /// Ends the current session (revokes its refresh token and clears the cookies).
        /// </summary>
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await _sessionService.LogoutAsync(Request, Response);
            return NoContent();
        }

        /// <summary>
        /// Ends all sessions on every device: revokes all refresh tokens and invalidates
        /// every access token issued so far.
        /// </summary>
        [Authorize]
        [AdminAudit("auth.logout_all", "auth")]
        [HttpPost("logout-all")]
        public async Task<IActionResult> LogoutAll()
        {
            var revoked = await _sessionService.RevokeAllAsync(_adminCredentials.AdminEmail);
            await _tokenRevocation.RevokeAllIssuedUntilNowAsync(_systemConfig);
            _sessionService.ClearCookies(Response);
            _logger.LogWarning("All sessions revoked by {Email} ({Count} refresh tokens)", _adminCredentials.AdminEmail, revoked);
            return NoContent();
        }

        /// <summary>
        /// Registers a failed attempt for the IP (and account, when known) using configured limits.
        /// Account lockout uses a higher threshold so a single attacker cannot trivially lock the admin out.
        /// </summary>
        private async Task RegisterFailureAsync(string ip, string? accountKey)
        {
            var maxAttempts = await _systemConfig.GetIntAsync("Bruteforce.MaxAttempts", 5);
            var lockoutMinutes = await _systemConfig.GetIntAsync("Bruteforce.LockoutMinutes", 5);

            _bruteforceProtection.RegisterFailure(ip, maxAttempts, lockoutMinutes);
            if (accountKey != null)
            {
                _bruteforceProtection.RegisterFailure(accountKey, maxAttempts * 4, lockoutMinutes);
            }
        }

        /// <summary>
        /// Builds the bruteforce tracking key for an account.
        /// </summary>
        private static string GetAccountKey(string? email)
            => "account:" + (email ?? string.Empty).Trim().ToLowerInvariant();

        /// <summary>
        /// Resolves caller IP address for bruteforce tracking and audit logs.
        /// </summary>
        private string GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
