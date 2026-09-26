using BotManager.Backend.API.Services;
using BotManager.Backend.Services.Interfaces;
using BotManager.Backend.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BotManager.Backend.API.Controllers
{
    /// <summary>
    /// Manages TOTP two-factor authentication of the admin account.
    /// </summary>
    [ApiController]
    [Route("api/auth/2fa")]
    [Authorize]
    public class TwoFactorController : ControllerBase
    {
        private readonly TwoFactorService _twoFactor;
        private readonly AdminCredentialsService _adminCredentials;
        private readonly IAdminAuditService _audit;
        private readonly IBruteforceProtectionService _bruteforceProtection;
        private readonly ISystemConfigService _systemConfig;
        private readonly ILogger<TwoFactorController> _logger;

        /// <summary>
        /// Creates a new two-factor controller.
        /// </summary>
        public TwoFactorController(TwoFactorService twoFactor, AdminCredentialsService adminCredentials,
            IAdminAuditService audit, IBruteforceProtectionService bruteforceProtection,
            ISystemConfigService systemConfig, ILogger<TwoFactorController> logger)
        {
            _twoFactor = twoFactor;
            _adminCredentials = adminCredentials;
            _audit = audit;
            _bruteforceProtection = bruteforceProtection;
            _systemConfig = systemConfig;
            _logger = logger;
        }

        /// <summary>Returns whether 2FA is enabled and how many recovery codes remain.</summary>
        [HttpGet("status")]
        public async Task<IActionResult> Status()
        {
            var status = await _twoFactor.GetStatusAsync();
            return Ok(new
            {
                enabled = status.Enabled,
                pendingSetup = status.PendingSetup,
                recoveryCodesRemaining = status.RecoveryCodesRemaining
            });
        }

        /// <summary>
        /// Starts a setup: returns a new secret and otpauth:// URI. 2FA is enforced only after <see cref="Enable"/>.
        /// </summary>
        [HttpPost("setup")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> Setup()
        {
            var setup = await _twoFactor.BeginSetupAsync(_adminCredentials.AdminEmail);
            if (setup == null)
            {
                return Conflict(new { message = "Two-factor authentication is already enabled." });
            }

            await _audit.LogAsync(HttpContext, "auth.2fa.setup", "auth");
            return Ok(new { secret = setup.Secret, otpAuthUri = setup.OtpAuthUri });
        }

        /// <summary>
        /// Confirms the pending setup with a code and enables 2FA. The recovery codes are returned only here.
        /// </summary>
        [HttpPost("enable")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> Enable([FromBody] TwoFactorEnableRequest request)
        {
            if (IsLocked())
            {
                return StatusCode(429, new { message = "Too many attempts. Please try again later." });
            }

            var codes = await _twoFactor.EnableAsync(request.Code);
            if (codes == null)
            {
                await RegisterFailureAsync();
                return BadRequest(new { message = "Invalid code or no pending setup.", invalidCode = true });
            }

            await _audit.LogAsync(HttpContext, "auth.2fa.enable", "auth");
            _logger.LogWarning("Two-factor authentication enabled for {Email}", _adminCredentials.AdminEmail);
            return Ok(new { recoveryCodes = codes });
        }

        /// <summary>
        /// Disables 2FA. Requires the password and a current TOTP code or an unused recovery code.
        /// </summary>
        [HttpPost("disable")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> Disable([FromBody] TwoFactorDisableRequest request)
        {
            if (IsLocked())
            {
                return StatusCode(429, new { message = "Too many attempts. Please try again later." });
            }

            if (!await _twoFactor.IsEnabledAsync())
            {
                return Conflict(new { message = "Two-factor authentication is not enabled." });
            }

            // Evaluate both factors before responding so the response does not reveal which one was wrong.
            var passwordOk = _adminCredentials.Verify(_adminCredentials.AdminEmail, request.Password);
            var codeOk = passwordOk
                && (!string.IsNullOrWhiteSpace(request.Code) || !string.IsNullOrWhiteSpace(request.RecoveryCode))
                && await _twoFactor.VerifyAsync(request.Code, request.RecoveryCode) != TwoFactorCheckResult.Invalid;
            if (!codeOk)
            {
                await RegisterFailureAsync();
                return BadRequest(new { message = "Invalid password or code.", invalidCode = true });
            }

            await _twoFactor.DisableAsync();
            await _audit.LogAsync(HttpContext, "auth.2fa.disable", "auth");
            _logger.LogWarning("Two-factor authentication disabled for {Email}", _adminCredentials.AdminEmail);
            return NoContent();
        }

        private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        private bool IsLocked() => _bruteforceProtection.IsLocked(Ip);

        private async Task RegisterFailureAsync()
        {
            var maxAttempts = await _systemConfig.GetIntAsync("Bruteforce.MaxAttempts", 5);
            var lockoutMinutes = await _systemConfig.GetIntAsync("Bruteforce.LockoutMinutes", 5);
            _bruteforceProtection.RegisterFailure(Ip, maxAttempts, lockoutMinutes);
        }
    }

    /// <summary>Request body of POST /api/auth/2fa/enable.</summary>
    public sealed class TwoFactorEnableRequest
    {
        /// <summary>Code from the authenticator app.</summary>
        public string? Code { get; set; }
    }

    /// <summary>Request body of POST /api/auth/2fa/disable.</summary>
    public sealed class TwoFactorDisableRequest
    {
        /// <summary>Current TOTP code.</summary>
        public string? Code { get; set; }

        /// <summary>Unused recovery code (alternative to <see cref="Code"/>).</summary>
        public string? RecoveryCode { get; set; }

        /// <summary>Admin password.</summary>
        public string? Password { get; set; }
    }
}
