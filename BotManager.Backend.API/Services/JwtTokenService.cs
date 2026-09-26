using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Issues signed JWT access tokens and owns the shared signing key configuration.
    /// </summary>
    public class JwtTokenService
    {
        /// <summary>Minimum signing secret length in bytes (HMAC-SHA256 key size).</summary>
        public const int MinimumSecretBytes = 32;

        /// <summary>Default access token lifetime when JwtSettings:ExpiryMinutes is not configured.</summary>
        public const int DefaultExpiryMinutes = 480;

        private readonly SigningCredentials _credentials;
        private readonly string? _issuer;
        private readonly string? _audience;
        private readonly TimeSpan _lifetime;

        /// <summary>
        /// Creates a new token service from configuration. Throws when the secret is missing or too short.
        /// </summary>
        public JwtTokenService(IConfiguration configuration)
        {
            _credentials = new SigningCredentials(CreateSigningKey(configuration), SecurityAlgorithms.HmacSha256);
            _issuer = configuration["JwtSettings:Issuer"];
            _audience = configuration["JwtSettings:Audience"];

            var expiryMinutes = int.TryParse(configuration["JwtSettings:ExpiryMinutes"], out var parsed) && parsed > 0
                ? parsed
                : DefaultExpiryMinutes;
            _lifetime = TimeSpan.FromMinutes(expiryMinutes);
        }

        /// <summary>
        /// Builds the symmetric signing key from JwtSettings:Secret, validating its length.
        /// </summary>
        public static SymmetricSecurityKey CreateSigningKey(IConfiguration configuration)
        {
            var secret = configuration["JwtSettings:Secret"];
            if (string.IsNullOrEmpty(secret))
                throw new InvalidOperationException("JwtSettings:Secret is missing");

            var bytes = Encoding.UTF8.GetBytes(secret);
            if (bytes.Length < MinimumSecretBytes)
                throw new InvalidOperationException(
                    $"JwtSettings:Secret must be at least {MinimumSecretBytes} bytes long (use a random value).");

            return new SymmetricSecurityKey(bytes);
        }

        /// <summary>
        /// Generates a signed admin JWT for a successfully authenticated user.
        /// </summary>
        public string GenerateToken(string email)
        {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, email),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(ClaimTypes.Role, AuthRoles.Admin),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                notBefore: now,
                expires: now.Add(_lifetime),
                signingCredentials: _credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    /// <summary>
    /// Role names used in authorization policies.
    /// </summary>
    public static class AuthRoles
    {
        /// <summary>Administrator role issued to the configured admin account.</summary>
        public const string Admin = "Admin";
    }
}
