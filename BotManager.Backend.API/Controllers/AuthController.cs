using BotManager.Backend.API.Models;
using BotManager.Backend.API.Services;
using BotManager.Backend.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BotManager.Backend.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IBruteforceProtectionService _bruteforceProtection;
        private readonly AuthService _authService;
        private readonly ILogger<AuthController> _logger;

        /// <summary>
        /// Creates a new authentication controller.
        /// </summary>
        public AuthController(IConfiguration configuration, IBruteforceProtectionService bruteforceProtection,
            AuthService authService, ILogger<AuthController> logger)
        {
            _configuration = configuration;
            _bruteforceProtection = bruteforceProtection;
            _authService = authService;
            _logger = logger;
        }

        /// <summary>Returns the current failed login attempts for the caller's IP (for smart captcha)</summary>
        [HttpGet("attempt-status")]
        public IActionResult GetAttemptStatus()
        {
            var ip = GetIp();
            var locked = _bruteforceProtection.IsLocked(ip);
            var attempts = _bruteforceProtection.GetFailedAttempts(ip);
            return Ok(new { locked, attempts });
        }

        /// <summary>
        /// Authenticates admin credentials using configured email/password.
        /// </summary>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var ip = GetIp();

            if (_bruteforceProtection.IsLocked(ip))
            {
                await _authService.LogLoginAttemptAsync(request.Email ?? "", ip, false,
                    "Bruteforce lockout", isBruteforce: true);
                _logger.LogWarning("Login blocked for IP {Ip} (bruteforce)", ip);
                return StatusCode(429, "Too many login attempts. Please try again later.");
            }

            var adminEmail = _configuration["AdminCredentials:Email"];
            var adminPassword = _configuration["AdminCredentials:Password"];

            if (request.Email == adminEmail && request.Password == adminPassword)
            {
                var email = request.Email ?? string.Empty;
                _bruteforceProtection.RegisterSuccess(ip);
                await _authService.LogLoginAttemptAsync(email, ip, true);
                _logger.LogInformation("User {Email} logged in successfully from {Ip}", email, ip);
                return Ok(new LoginResponse { Token = GenerateJwtToken(email), Email = email });
            }

            _bruteforceProtection.RegisterFailure(ip);
            await _authService.LogLoginAttemptAsync(request.Email ?? "", ip, false, "Nesprávné přihlašovací údaje");
            _logger.LogWarning("Failed login attempt for {Email} from {Ip}", request.Email, ip);
            return Unauthorized("Invalid credentials.");
        }

        /// <summary>
        /// Authenticates the configured admin account using Google ID token validation.
        /// </summary>
        [HttpPost("google-login")]
        public async Task<IActionResult> GoogleLogin([FromBody] LoginRequest request)
        {
            var ip = GetIp();

            if (string.IsNullOrEmpty(request.GoogleIdToken))
            {
                return BadRequest("Google ID token is required.");
            }

            var payload = await _authService.VerifyGoogleTokenAsync(request.GoogleIdToken);
            if (payload == null)
            {
                await _authService.LogLoginAttemptAsync(request.Email ?? "", ip, false, "Neplatný Google token");
                return Unauthorized("Invalid Google token.");
            }

            var adminEmail = _configuration["AdminCredentials:Email"];
            if (payload.Email != adminEmail)
            {
                await _authService.LogLoginAttemptAsync(payload.Email, ip, false, "Neautorizovaný Google účet");
                _logger.LogWarning("Unauthorized Google account login attempt: {Email}", payload.Email);
                return Unauthorized("Unauthorized Google account.");
            }

            await _authService.LogLoginAttemptAsync(payload.Email, ip, true);
            _logger.LogInformation("User {Email} logged in via Google from {Ip}", payload.Email, ip);
            return Ok(new LoginResponse { Token = GenerateJwtToken(payload.Email), Email = payload.Email });
        }

        /// <summary>
        /// Generates a signed JWT token for a successfully authenticated user.
        /// </summary>
        private string GenerateJwtToken(string email)
        {
            var secret = _configuration["JwtSettings:Secret"];
            if (string.IsNullOrEmpty(secret)) throw new InvalidOperationException("JWT Secret not configured.");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, email),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: _configuration["JwtSettings:Issuer"],
                audience: _configuration["JwtSettings:Audience"],
                claims: claims,
                expires: DateTime.Now.AddDays(1),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// Resolves caller IP address for bruteforce tracking and audit logs.
        /// </summary>
        private string GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
