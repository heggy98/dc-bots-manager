namespace BotManager.Backend.Services.Interfaces
{
    /// <summary>
    /// Defines failed-login tracking and lockout operations.
    /// </summary>
    public interface IBruteforceProtectionService
    {
        /// <summary>
        /// Returns whether an IP address is currently locked out.
        /// </summary>
        bool IsLocked(string ipAddress);

        /// <summary>
        /// Registers a failed login attempt for an IP address.
        /// </summary>
        void RegisterFailure(string ipAddress);

        /// <summary>
        /// Clears failed-login tracking for an IP address.
        /// </summary>
        void RegisterSuccess(string ipAddress);

        /// <summary>
        /// Gets count of failed attempts for an IP address.
        /// </summary>
        int GetFailedAttempts(string ipAddress);
    }
}
