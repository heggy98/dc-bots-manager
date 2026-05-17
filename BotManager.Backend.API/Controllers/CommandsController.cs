using BotManager.Backend.API.Models;
using BotManager.Backend.API.Services;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CommandsController : ControllerBase
    {
        private readonly BotManagerDbContext _db;
        private readonly IUserIdentityResolver _userIdentityResolver;

        /// <summary>
        /// Creates a new commands controller.
        /// </summary>
        public CommandsController(BotManagerDbContext db, IUserIdentityResolver userIdentityResolver)
        {
            _db = db;
            _userIdentityResolver = userIdentityResolver;
        }

        /// <summary>
        /// Returns globally grouped command settings for all bots owned by the current user.
        /// </summary>
        [HttpGet("global")]
        public async Task<IActionResult> GetGlobalCommands()
        {
            var ownerUserId = ResolveOwnerUserIdOrNull();
            if (ownerUserId == null)
            {
                return Unauthorized("Unable to resolve current user identity.");
            }

            var commands = await _db.BotCommands
                .Where(c => c.Bot != null && c.Bot.OwnerUserId == ownerUserId)
                .OrderBy(c => c.CommandName)
                .ToListAsync();

            var grouped = commands
                .GroupBy(c => new { c.CommandName, c.SubCommandName })
                .Select(group =>
                {
                    var representative = group
                        .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
                        .First();

                    return new GlobalCommandDto
                    {
                        CommandName = group.Key.CommandName,
                        SubCommandName = group.Key.SubCommandName,
                        Description = representative.Description,
                        MinimumPermissionLevel = representative.MinimumPermissionLevel,
                        IsEnabled = representative.IsEnabled,
                        UserHint = representative.UserHint,
                        SuccessMessage = representative.SuccessMessage,
                        PermissionMessage = representative.PermissionMessage,
                        ErrorMessage = representative.ErrorMessage,
                        AdminOnlyMessage = representative.AdminOnlyMessage,
                        InvalidArgumentsMessage = representative.InvalidArgumentsMessage,
                        BotCount = group.Select(c => c.BotId).Distinct().Count(),
                        HasDifferencesAcrossBots = HasDifferences(group)
                    };
                })
                .OrderBy(c => c.CommandName)
                .ThenBy(c => c.SubCommandName ?? string.Empty)
                .ToList();

            return Ok(grouped);
        }

        /// <summary>
        /// Updates a command definition across all bots owned by the current user.
        /// </summary>
        [HttpPut("global/{commandName}")]
        public async Task<IActionResult> UpdateGlobalCommand(string commandName, [FromQuery] string? subCommandName, [FromBody] UpdateGlobalCommandDto request)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return BadRequest("Command name is required.");
            }

            var ownerUserId = ResolveOwnerUserIdOrNull();
            if (ownerUserId == null)
            {
                return Unauthorized("Unable to resolve current user identity.");
            }

            var commands = await _db.BotCommands
                .Where(c => c.CommandName == commandName
                    && c.SubCommandName == subCommandName
                    && c.Bot != null
                    && c.Bot.OwnerUserId == ownerUserId)
                .ToListAsync();

            if (commands.Count == 0)
            {
                return NotFound();
            }

            var now = DateTime.UtcNow;
            foreach (var command in commands)
            {
                command.Description = request.Description;
                command.MinimumPermissionLevel = request.MinimumPermissionLevel;
                command.IsEnabled = request.IsEnabled;
                command.UserHint = request.UserHint;
                command.SuccessMessage = request.SuccessMessage;
                command.PermissionMessage = request.PermissionMessage;
                command.ErrorMessage = request.ErrorMessage;
                command.AdminOnlyMessage = request.AdminOnlyMessage;
                command.InvalidArgumentsMessage = request.InvalidArgumentsMessage;
                command.UpdatedAt = now;
            }

            await _db.SaveChangesAsync();
            return Ok(new { updated = commands.Count });
        }

        /// <summary>
        /// Checks whether command settings differ across bot-specific command rows.
        /// </summary>
        private static bool HasDifferences(IEnumerable<BotCommand> group)
        {
            var first = group.First();
            return group.Any(c =>
                c.Description != first.Description ||
                c.MinimumPermissionLevel != first.MinimumPermissionLevel ||
                c.IsEnabled != first.IsEnabled ||
                c.UserHint != first.UserHint ||
                c.SuccessMessage != first.SuccessMessage ||
                c.PermissionMessage != first.PermissionMessage ||
                c.ErrorMessage != first.ErrorMessage ||
                c.AdminOnlyMessage != first.AdminOnlyMessage ||
                c.InvalidArgumentsMessage != first.InvalidArgumentsMessage);
        }

        /// <summary>
        /// Resolves the caller identity used to scope ownership queries.
        /// </summary>
        private string? GetCurrentUserIdentifier()
        {
            return _userIdentityResolver.GetCurrentUserIdentifier(User);
        }

        /// <summary>
        /// Resolves owner user id and normalizes missing/empty values to null.
        /// </summary>
        private string? ResolveOwnerUserIdOrNull()
        {
            var ownerUserId = GetCurrentUserIdentifier();
            return string.IsNullOrWhiteSpace(ownerUserId) ? null : ownerUserId;
        }
    }
}
