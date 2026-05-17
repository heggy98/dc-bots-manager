using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Default claims-based resolver for current authenticated user identity.
    /// </summary>
    public class UserIdentityResolver : IUserIdentityResolver
    {
        public string? GetCurrentUserIdentifier(ClaimsPrincipal user)
        {
            return user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue(ClaimTypes.Email)
                ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? user.FindFirstValue(JwtRegisteredClaimNames.Email);
        }
    }
}