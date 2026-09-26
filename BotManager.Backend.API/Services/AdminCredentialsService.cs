using BotManager.Backend.Services.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Validates the configured admin account credentials.
    /// Production expects AdminCredentials:PasswordHash (BCrypt); a plaintext
    /// AdminCredentials:Password is accepted only in the Development environment.
    /// </summary>
    public class AdminCredentialsService
    {
        private readonly string? _email;
        private readonly string? _passwordHash;
        private readonly string? _plainPassword;
        private readonly IPasswordHasherService _passwordHasher;

        /// <summary>
        /// Creates a new admin credentials service.
        /// </summary>
        public AdminCredentialsService(IConfiguration configuration, IHostEnvironment environment,
            IPasswordHasherService passwordHasher)
        {
            _passwordHasher = passwordHasher;
            _email = configuration["AdminCredentials:Email"]?.Trim();
            _passwordHash = configuration["AdminCredentials:PasswordHash"];
            _plainPassword = environment.IsDevelopment() ? configuration["AdminCredentials:Password"] : null;

            if (string.IsNullOrWhiteSpace(_email))
                throw new InvalidOperationException("AdminCredentials:Email is not configured.");

            if (string.IsNullOrWhiteSpace(_passwordHash) && string.IsNullOrEmpty(_plainPassword))
                throw new InvalidOperationException(environment.IsDevelopment()
                    ? "AdminCredentials:PasswordHash (or AdminCredentials:Password in Development) is not configured."
                    : "AdminCredentials:PasswordHash is not configured. Generate one with: dotnet run -- --hash-password <password>");
        }

        /// <summary>The configured admin email.</summary>
        public string AdminEmail => _email!;

        /// <summary>
        /// Returns whether the given email belongs to the admin account (case-insensitive).
        /// </summary>
        public bool IsAdminEmail(string? email)
            => !string.IsNullOrWhiteSpace(email)
               && string.Equals(email.Trim(), _email, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Verifies email and password against the configured admin credentials.
        /// </summary>
        public bool Verify(string? email, string? password)
        {
            if (string.IsNullOrEmpty(password))
                return false;

            // Always evaluate the password so timing does not reveal whether the email matched.
            bool passwordOk;
            if (!string.IsNullOrWhiteSpace(_passwordHash))
            {
                passwordOk = _passwordHasher.VerifyPassword(password, _passwordHash);
            }
            else
            {
                passwordOk = CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(password),
                    Encoding.UTF8.GetBytes(_plainPassword!));
            }

            return passwordOk && IsAdminEmail(email);
        }
    }
}
