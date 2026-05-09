namespace BotManager.Api.Models
{
    public class LoginRequest
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? RecaptchaToken { get; set; }
        /// <summary>Google ID Token from the Google Sign-In flow</summary>
        public string? GoogleIdToken { get; set; }
    }
}
