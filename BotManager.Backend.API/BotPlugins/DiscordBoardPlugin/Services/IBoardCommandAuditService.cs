using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Shared.Models;
using Discord;

namespace BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Services
{
    /// <summary>
    /// Shared helper for command metadata lookup and usage audit logging.
    /// </summary>
    public interface IBoardCommandAuditService
    {
        Task<BotCommand?> GetCommandAsync(string commandName, IPluginContext context);
        Task LogCommandUsageAsync(BotCommand? command, IUser user, bool isSuccess, string? errorMessage, IPluginContext context);
    }
}