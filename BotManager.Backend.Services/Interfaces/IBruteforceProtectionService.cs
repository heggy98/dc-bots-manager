namespace BotManager.Backend.Services.Interfaces
{
    /// <summary>
    /// Defines failed-login tracking and lockout operations.
    /// Keys are opaque strings (e.g. an IP address or an account identifier).
    /// </summary>
    public interface IBruteforceProtectionService
    {
        /// <summary>
        /// Returns whether a key is currently locked out.
        /// </summary>
        bool IsLocked(string key);

        /// <summary>
        /// Registers a failed login attempt for a key using default limits.
        /// </summary>
        void RegisterFailure(string key);

        /// <summary>
        /// Registers a failed login attempt for a key using explicit limits.
        /// </summary>
        void RegisterFailure(string key, int maxAttempts, int lockoutMinutes);

        /// <summary>
        /// Clears failed-login tracking for a key.
        /// </summary>
        void RegisterSuccess(string key);

        /// <summary>
        /// Gets count of failed attempts for a key.
        /// </summary>
        int GetFailedAttempts(string key);
    }
}
