using BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Services;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Shared.Models;
using Discord;

namespace BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Handlers
{
    /// <summary>
    /// Base class for board handlers that need command metadata and usage logging.
    /// </summary>
    public abstract class BoardCommandHandlerBase
    {
        private readonly IBoardCommandAuditService _boardCommandAuditService;

        protected BoardCommandHandlerBase(IBoardCommandAuditService boardCommandAuditService)
        {
            _boardCommandAuditService = boardCommandAuditService;
        }

        protected Task<BotCommand?> GetCommandAsync(string commandName, IPluginContext context)
        {
            return _boardCommandAuditService.GetCommandAsync(commandName, context);
        }

        protected Task LogCommandUsageAsync(BotCommand? command, IUser user, bool isSuccess, string? errorMessage, IPluginContext context)
        {
            return _boardCommandAuditService.LogCommandUsageAsync(command, user, isSuccess, errorMessage, context);
        }
    }
}