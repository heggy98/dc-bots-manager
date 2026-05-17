namespace BotManager.Backend.Services.Interfaces
{
    /// <summary>
    /// Defines password hashing and verification operations.
    /// </summary>
    public interface IPasswordHasherService
    {
        /// <summary>
        /// Hashes a plain-text password for storage.
        /// </summary>
        string HashPassword(string password);

        /// <summary>
        /// Verifies a plain-text password against a stored hash.
        /// </summary>
        bool VerifyPassword(string password, string hashedPassword);
    }
}
