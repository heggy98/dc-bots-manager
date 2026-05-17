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

            var res = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
            Console.WriteLine($"Vygenerovaný Hash: {res}");
            return res;
        }

        /// <summary>
        /// Verifies a plain-text password against a stored BCrypt hash.
        /// </summary>
        public bool VerifyPassword(string password, string hashedPassword)
        {
            RequirePassword(password);
            RequireHashedPassword(hashedPassword);

            return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
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
            if (hashedPassword == null)
            {
                throw new ArgumentException(nameof(hashedPassword));
            }
        }
    }
}
