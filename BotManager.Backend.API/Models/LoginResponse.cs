namespace BotManager.Backend.API.Models
{
    /// <summary>
    /// Session info returned after login/refresh. Tokens themselves are only sent as HttpOnly cookies.
    /// </summary>
    public class LoginResponse
    {
        public string Email { get; set; } = string.Empty;
        public DateTime AccessTokenExpiresAt { get; set; }
        public DateTime RefreshTokenExpiresAt { get; set; }
    }
}
