using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Shared.Models;
using Discord;

namespace BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Services
{
    /// <summary>
    /// Performs command definition lookups and command usage logging for board handlers.
    /// </summary>
    public class BoardCommandAuditService : IBoardCommandAuditService
    {
        public async Task<BotCommand?> GetCommandAsync(string commandName, IPluginContext context)
        {
            try
            {
                var commandService = context.ServiceProvider.GetService(typeof(CommandManagementService)) as CommandManagementService;
                if (commandService == null)
                {
                    return null;
                }

                return await commandService.GetCommandAsync(context.Bot.BotId, commandName);
            }
            catch (Exception ex)
            {
                context.Logger.LogWarning(ex, "BotId={BotId}: Failed to retrieve command '{CommandName}'", context.Bot.BotId, commandName);
                return null;
            }
        }

        public async Task LogCommandUsageAsync(BotCommand? command, IUser user, bool isSuccess, string? errorMessage, IPluginContext context)
        {
            if (command == null)
            {
                return;
            }

            try
            {
                var commandService = context.ServiceProvider.GetService(typeof(CommandManagementService)) as CommandManagementService;
                if (commandService == null)
                {
                    return;
                }

                await commandService.LogCommandUsageAsync(command.CommandId, user.Id, user.Username, isSuccess, errorMessage);
            }
            catch (Exception ex)
            {
                context.Logger.LogWarning(ex, "BotId={BotId}: Failed to log command usage for command '{CommandId}'", context.Bot.BotId, command?.CommandId);
            }
        }
    }
}