using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Writes administrator actions to the AdminAuditLogs table.
    /// </summary>
    public interface IAdminAuditService
    {
        /// <summary>
        /// Records an action performed in the given request. Failures are logged and swallowed so auditing
        /// never breaks the audited operation. Never pass secrets in <paramref name="details"/>.
        /// </summary>
        Task LogAsync(HttpContext httpContext, string action, string? targetType = null, string? targetId = null,
            string? details = null);
    }

    /// <summary>
    /// Default EF Core implementation of <see cref="IAdminAuditService"/>.
    /// </summary>
    public class AdminAuditService : IAdminAuditService
    {
        private const int MaxDetailsLength = 2000;

        private readonly BotManagerDbContext _db;
        private readonly ILogger<AdminAuditService> _logger;

        /// <summary>
        /// Creates a new admin audit service.
        /// </summary>
        public AdminAuditService(BotManagerDbContext db, ILogger<AdminAuditService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task LogAsync(HttpContext httpContext, string action, string? targetType = null,
            string? targetId = null, string? details = null)
        {
            var entry = new AdminAuditLog
            {
                Timestamp = DateTime.UtcNow,
                ActorEmail = Truncate(GetActor(httpContext.User), 200) ?? string.Empty,
                Action = Truncate(action, 100)!,
                TargetType = Truncate(targetType, 50),
                TargetId = Truncate(targetId, 200),
                Details = Truncate(details, MaxDetailsLength),
                IpAddress = Truncate(httpContext.Connection.RemoteIpAddress?.ToString(), 50)
            };

            try
            {
                _db.AdminAuditLogs.Add(entry);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _db.Entry(entry).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                _logger.LogWarning(ex, "Failed to write admin audit entry {Action}", action);
            }
        }

        private static string? GetActor(ClaimsPrincipal user)
            => user.FindFirstValue(ClaimTypes.Email)
               ?? user.FindFirstValue(JwtRegisteredClaimNames.Email)
               ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        private static string? Truncate(string? value, int max)
            => value == null || value.Length <= max ? value : value[..max];
    }

    /// <summary>
    /// Records an admin audit entry after the decorated action completed with a 2xx result.
    /// The target id is taken from the route value <see cref="TargetIdRouteKey"/> (default "id");
    /// other route values and the action arguments listed in <see cref="DetailArguments"/> are stored as JSON details.
    /// Only list arguments that never contain secrets.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class AdminAuditAttribute : Attribute, IAsyncActionFilter
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// Creates the attribute for an action name such as "bot.start".
        /// </summary>
        public AdminAuditAttribute(string action, string? targetType = null)
        {
            Action = action;
            TargetType = targetType;
        }

        /// <summary>Audit action name.</summary>
        public string Action { get; }

        /// <summary>Kind of the affected object.</summary>
        public string? TargetType { get; }

        /// <summary>Route value holding the target id.</summary>
        public string TargetIdRouteKey { get; set; } = "id";

        /// <summary>When the route has no target id, use an int/long returned by an OkObjectResult (create endpoints).</summary>
        public bool TargetIdFromResult { get; set; }

        /// <summary>Names of (non-secret) action arguments to include in the details.</summary>
        public string[] DetailArguments { get; set; } = [];

        /// <inheritdoc />
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var details = new Dictionary<string, object?>();
            foreach (var (key, value) in context.RouteData.Values)
            {
                if (key is "controller" or "action" || key == TargetIdRouteKey)
                    continue;
                details[key] = value?.ToString();
            }

            foreach (var name in DetailArguments)
            {
                if (context.ActionArguments.TryGetValue(name, out var value))
                    details[name] = value;
            }

            var executed = await next();
            if (executed.Exception != null && !executed.ExceptionHandled)
                return;

            var status = executed.Result is IStatusCodeActionResult withStatus ? withStatus.StatusCode ?? 200 : 200;
            if (status is < 200 or >= 300)
                return;

            var targetId = context.RouteData.Values.TryGetValue(TargetIdRouteKey, out var routeId)
                ? routeId?.ToString()
                : null;
            if (targetId == null && TargetIdFromResult && executed.Result is ObjectResult { Value: int or long } created)
                targetId = created.Value!.ToString();

            var audit = context.HttpContext.RequestServices.GetRequiredService<IAdminAuditService>();
            await audit.LogAsync(context.HttpContext, Action, TargetType, targetId,
                details.Count == 0 ? null : JsonSerializer.Serialize(details, JsonOptions));
        }
    }
}
