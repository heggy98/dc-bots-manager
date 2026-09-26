using BotManager.Backend.Services.Interfaces;

namespace BotManager.Backend.Services.Implementation
{
    /// <summary>
    /// Provides BCrypt-based password hashing and verification.
    /// </summary>
    public class PasswordHasherService : IPasswordHasherService
    {
        /// <summary>
        /// Hashes a plain-text password using BCrypt.
        /// </summary>
        public string HashPassword(string password)
        {
            RequirePassword(password);

            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        }

        /// <summary>
        /// Verifies a plain-text password against a stored BCrypt hash.
        /// </summary>
        public bool VerifyPassword(string password, string hashedPassword)
        {
            RequirePassword(password);
            RequireHashedPassword(hashedPassword);

            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return false;
            }
        }

        /// <summary>
        /// Validates that a plain-text password is present.
        /// </summary>
        private static void RequirePassword(string password)
        {
            if (password == null)
            {
                throw new ArgumentNullException(nameof(password));
            }

            if (password.Length == 0)
            {
                throw new ArgumentException("Password cannot be empty", nameof(password));
            }
        }

        /// <summary>
        /// Validates that a password hash is present.
        /// </summary>
        private static void RequireHashedPassword(string hashedPassword)
        {
            if (string.IsNullOrWhiteSpace(hashedPassword))
            {
                throw new ArgumentException("Password hash cannot be empty", nameof(hashedPassword));
            }
        }
    }
}
