using Discord;
using Discord.WebSocket;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Contracts.Models;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Api.BotPlugins.DiscordBotAlliancePlugin.Handlers
{
    /// <summary>
    /// Handles emoji-related commands: /reorganize-emojis
    /// </summary>
    public class EmojisCommandHandler
    {
        private static readonly List<string> AvailableEmojis = new List<string>
        {
            "🔴", "🟠", "🟡", "🟢", "🔵", "🟣", "🟤", "⚪", "⚫",
            "🎯", "⭐", "✨", "💫", "🌟", "🎭", "🎪", "🎨", "🎬",
            "🏆", "🥇", "🥈", "🥉", "🎖️", "🏅", "🎗️", "🎀", "🎁",
            "❤️", "🧡", "💛", "💚", "💙", "💜", "🖤", "🤍", "🤎"
        };

        /// <summary>
        /// Handle /reorganize-emojis command - reorganize team emojis
        /// </summary>
        public async Task HandleReorganizeEmojisAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, PluginContext context)
        {
            // Get command definition from database for localized strings
            var emojiCommand = await GetCommandAsync("reorganize-emojis", context);
            var adminOnlyMsg = emojiCommand?.AdminOnlyMessage ?? "Tento příkaz je pouze pro administrátory!";
            var successMsg = emojiCommand?.SuccessMessage ?? "✓ Emojis byly úspěšně reorganizovány!";
            var errorMsg = emojiCommand?.ErrorMessage ?? "Chyba: {0}";

            // Check if user is admin (admin role or guild owner)
            var guildUser = command.User as SocketGuildUser;
            if (!guildUser?.GuildPermissions.Administrator ?? true && command.User.Id != guild.OwnerId)
            {
                await command.FollowupAsync(adminOnlyMsg, ephemeral: true);
                await LogCommandUsageAsync(emojiCommand, command.User, false, "Permission denied", context);
                return;
            }

            try
            {
                // Load teams
                var teamsData = await context.TeamsDataService.GetAsync(context.Bot.BotId);

                if (!teamsData.Teams.Any())
                {
                    var emptyMsg = emojiCommand?.InvalidArgumentsMessage ?? "Nejsou registrovány žádné týmy!";
                    await command.FollowupAsync(emptyMsg, ephemeral: true);
                    await LogCommandUsageAsync(emojiCommand, command.User, false, "No teams available", context);
                    return;
                }

                // Assign new emojis to teams
                var emojiIndex = 0;
                foreach (var team in teamsData.Teams)
                {
                    if (emojiIndex < AvailableEmojis.Count)
                    {
                        team.Emoji = AvailableEmojis[emojiIndex];
                        emojiIndex++;
                    }
                    else
                    {
                        // Cycle through emojis if more teams than emojis
                        emojiIndex = 0;
                        team.Emoji = AvailableEmojis[emojiIndex];
                        emojiIndex++;
                    }
                }

                // Save updated teams
                await context.TeamsDataService.SaveAsync(context.Bot.BotId, teamsData);

                var discordBotService = context.ServiceProvider.GetService(typeof(IDiscordBotService)) as IDiscordBotService;
                if (discordBotService == null)
                {
                    await command.FollowupAsync("Discord bot service není dostupná, board zprávu nelze aktualizovat.", ephemeral: true);
                    await LogCommandUsageAsync(emojiCommand, command.User, false, "Discord bot service unavailable", context);
                    return;
                }

                var boardUpdated = await discordBotService.RefreshBoardMessageAsync(context.Bot.BotId, BoardMessageFactory.FromTeams(teamsData));

                await command.FollowupAsync(boardUpdated
                    ? "Board zpráva byla aktualizována."
                    : "Board zprávu se nepodařilo aktualizovat.", ephemeral: true);
                context.Logger.LogInformation("Emojis reorganized for {TeamCount} teams", teamsData.Teams.Count);
                await LogCommandUsageAsync(emojiCommand, command.User, true, null, context);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Error reorganizing emojis");
                await command.FollowupAsync(string.Format(errorMsg, ex.Message), ephemeral: true);
                await LogCommandUsageAsync(emojiCommand, command.User, false, ex.Message, context);
            }
        }

        /// <summary>
        /// Assign a new emoji to a team
        /// </summary>
        public static void AssignNewEmoji(Team team)
        {
            // Simple implementation: randomly select from available emojis
            var random = new Random();
            team.Emoji = AvailableEmojis[random.Next(AvailableEmojis.Count)];
        }

        /// <summary>
        /// Get next available emoji in the list
        /// </summary>
        public static string GetNextAvailableEmoji(int index)
        {
            if (index >= 0 && index < AvailableEmojis.Count)
                return AvailableEmojis[index];
            return AvailableEmojis[0];
        }

        /// <summary>
        /// Get command definition from database for localized strings
        /// </summary>
        private async Task<BotCommand?> GetCommandAsync(string commandName, PluginContext context)
        {
            try
            {
                var commandService = context.ServiceProvider.GetService(typeof(CommandManagementService)) as CommandManagementService;
                if (commandService == null) return null;

                return await commandService.GetCommandAsync(context.Bot.BotId, commandName);
            }
            catch (Exception ex)
            {
                context.Logger.LogWarning(ex, "Failed to retrieve command '{CommandName}'", commandName);
                return null;
            }
        }

        /// <summary>
        /// Log command usage to database
        /// </summary>
        private async Task LogCommandUsageAsync(BotCommand? command, IUser user, bool isSuccess, string? errorMessage, PluginContext context)
        {
            if (command == null) return;

            try
            {
                var commandService = context.ServiceProvider.GetService(typeof(CommandManagementService)) as CommandManagementService;
                if (commandService == null) return;

                await commandService.LogCommandUsageAsync(command.CommandId, user.Id, user.Username, isSuccess, errorMessage);
            }
            catch (Exception ex)
            {
                context.Logger.LogWarning(ex, "Failed to log command usage for command '{CommandId}'", command?.CommandId);
            }
        }

    }
}
