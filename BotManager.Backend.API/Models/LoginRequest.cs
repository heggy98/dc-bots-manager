namespace BotManager.Backend.API.Models
{
    public class LoginRequest
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? RecaptchaToken { get; set; }
        /// <summary>Google ID Token from the Google Sign-In flow</summary>
        public string? GoogleIdToken { get; set; }
        /// <summary>6-digit authenticator code, required when two-factor authentication is enabled</summary>
        public string? TotpCode { get; set; }
        /// <summary>Single-use recovery code, accepted instead of <see cref="TotpCode"/></summary>
        public string? RecoveryCode { get; set; }
    }
}
