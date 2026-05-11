using Discord;
using Discord.WebSocket;
using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Contracts.Models;
using BotManager.Backend.Entities.Entities;

namespace BotManager.Api.BotPlugins.DiscordBotAlliancePlugin.Handlers
{
    /// <summary>
    /// Handles reaction-related commands: /move-reactions
    /// </summary>
    public class ReactionsCommandHandler
    {
        /// <summary>
        /// Handle /move-reactions command - move reaction role message to another channel
        /// </summary>
        public async Task HandleMoveReactionMessageAsync(SocketSlashCommand command, SocketGuild guild, PluginContext context)
        {
            var targetChannelOption = command.Data.Options.FirstOrDefault(o => o.Name == "target-channel");
            var targetChannelValue = targetChannelOption?.Value as SocketTextChannel;

            // Get command definition from database for localized strings
            var reactionCommand = await GetCommandAsync("move-reactions", context);
            var adminOnlyMsg = reactionCommand?.AdminOnlyMessage ?? "Tento příkaz je pouze pro administrátory!";
            var userHint = reactionCommand?.UserHint ?? "Zadej cílový kanál pro zprávu.";
            var successMsg = reactionCommand?.SuccessMessage ?? "✓ Zpráva byla úspěšně přesunuta!";
            var errorMsg = reactionCommand?.ErrorMessage ?? "Chyba: {0}";

            // Check if user is admin
            var guildUser = command.User as SocketGuildUser;
            if (!guildUser?.GuildPermissions.Administrator ?? true && command.User.Id != guild.OwnerId)
            {
                await command.FollowupAsync(adminOnlyMsg, ephemeral: true);
                await LogCommandUsageAsync(reactionCommand, command.User, false, "Permission denied", context);
                return;
            }

            if (targetChannelValue == null)
            {
                await command.FollowupAsync(userHint, ephemeral: true);
                await LogCommandUsageAsync(reactionCommand, command.User, false, "No target channel specified", context);
                return;
            }

            try
            {
                // Load teams to recreate the message
                var teamsData = await context.TeamsDataService.GetAsync(context.Bot.BotId);

                if (!teamsData.Teams.Any())
                {
                    var emptyMsg = reactionCommand?.InvalidArgumentsMessage ?? "Nejsou registrovány žádné týmy!";
                    await command.FollowupAsync(emptyMsg, ephemeral: true);
                    await LogCommandUsageAsync(reactionCommand, command.User, false, "No teams available", context);
                    return;
                }

                // Create the role selection embed
                var embed = new EmbedBuilder()
                    .WithTitle("🎯 Výběr týmu")
                    .WithColor(Color.Blue)
                    .WithDescription("Reaguj emojisem pro přidělení role:")
                    .WithFooter($"Vytvořeno: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");

                foreach (var team in teamsData.Teams)
                {
                    var emoji = string.IsNullOrEmpty(team.Emoji) ? "🎯" : team.Emoji;
                    embed.AddField($"{emoji} {team.Name}", team.LeaderName, inline: false);
                }

                // Send new message to target channel
                var newMessage = await targetChannelValue.SendMessageAsync(embed: embed.Build());

                // Add all team emoji reactions to the new message
                foreach (var team in teamsData.Teams)
                {
                    var emoji = string.IsNullOrEmpty(team.Emoji) ? "🎯" : team.Emoji;
                    try
                    {
                        // Convert emoji string to Emoji object
                        if (Emoji.TryParse(emoji, out var parsedEmoji))
                        {
                            await newMessage.AddReactionAsync(parsedEmoji);
                        }
                    }
                    catch (Exception ex)
                    {
                        context.Logger.LogWarning(ex, "Failed to add reaction emoji '{Emoji}' to message", emoji);
                    }
                }

                context.Logger.LogInformation("Moved role selection message to channel {ChannelId}", targetChannelValue.Id);
                await command.FollowupAsync(string.Format(successMsg), ephemeral: true);
                await LogCommandUsageAsync(reactionCommand, command.User, true, null, context);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Error moving reaction message");
                await command.FollowupAsync(string.Format(errorMsg, ex.Message), ephemeral: true);
                await LogCommandUsageAsync(reactionCommand, command.User, false, ex.Message, context);
            }
        }

        /// <summary>
        /// Handle reaction added event - assign role when user reacts
        /// </summary>
        public async Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> cachedMessage, ISocketMessageChannel channel,
            SocketReaction reaction, SocketGuild guild, SocketGuildUser user, PluginContext context)
        {
            if (user.IsBot) return;

            try
            {
                // Get the message
                var message = await cachedMessage.GetOrDownloadAsync();
                if (message == null) return;

                // Load teams
                var teamsData = await context.TeamsDataService.GetAsync(context.Bot.BotId);
                var team = teamsData.Teams.FirstOrDefault(t => t.Emoji == reaction.Emote.Name);

                if (team == null) return;

                // Find or create role for the team
                var role = guild.Roles.FirstOrDefault(r => r.Name == team.Name);
                if (role == null)
                {
                    var randomColor = new Color((uint)Random.Shared.Next(0x1000000));
                    var createdRole = await guild.CreateRoleAsync(team.Name, color: randomColor);
                    context.Logger.LogInformation("Created role '{RoleName}' for team", team.Name);
                    // Try to get the role from the guild cache
                    role = guild.Roles.FirstOrDefault(r => r.Name == team.Name);
                }

                if (role != null)
                {
                    // Assign role to user
                    await user.AddRoleAsync(role);
                    context.Logger.LogInformation("Assigned role '{RoleName}' to user {UserId}", team.Name, user.Id);
                }
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Error handling reaction added event");
            }
        }

        /// <summary>
        /// Handle reaction removed event - remove role when user removes reaction
        /// </summary>
        public async Task HandleReactionRemovedAsync(Cacheable<IUserMessage, ulong> cachedMessage, ISocketMessageChannel channel,
            SocketReaction reaction, SocketGuild guild, SocketGuildUser user, PluginContext context)
        {
            if (user.IsBot) return;

            try
            {
                // Get the message
                var message = await cachedMessage.GetOrDownloadAsync();
                if (message == null) return;

                // Load teams
                var teamsData = await context.TeamsDataService.GetAsync(context.Bot.BotId);
                var team = teamsData.Teams.FirstOrDefault(t => t.Emoji == reaction.Emote.Name);

                if (team == null) return;

                // Find role for the team
                var role = guild.Roles.FirstOrDefault(r => r.Name == team.Name);
                if (role == null) return;

                // Remove role from user
                await user.RemoveRoleAsync(role);
                context.Logger.LogInformation("Removed role '{RoleName}' from user {UserId}", team.Name, user.Id);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Error handling reaction removed event");
            }
        }

        /// <summary>
        /// Find team by emoji
        /// </summary>
        private Task<Team?> FindTeamByEmojiAsync(string emojiName, PluginContext context)
        {
            return Task.FromResult<Team?>(null); // Placeholder
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
