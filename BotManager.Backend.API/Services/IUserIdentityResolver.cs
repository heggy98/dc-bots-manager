using System.Security.Claims;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Resolves caller identity from supported claim types.
    /// </summary>
    public interface IUserIdentityResolver
    {
        string? GetCurrentUserIdentifier(ClaimsPrincipal user);
    }
}