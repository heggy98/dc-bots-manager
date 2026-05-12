using Discord;
using Discord.WebSocket;
using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Shared.Models;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.BotPlugins.DiscordBotAlliancePlugin.Handlers
{
    /// <summary>
    /// Handles reaction-related commands: /move-reactions
    /// </summary>
    public class ReactionsCommandHandler
    {
        private const string BoardMessageMarker = "[DBA_BOARD]";

        /// <summary>
        /// Handle /board sync-reactions command - regenerate board message reactions from team emojis
        /// </summary>
        public async Task HandleSyncBoardReactionsAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, PluginContext context)
        {
            var syncCommand = await GetCommandAsync("sync-reactions", context);
            var adminOnlyMsg = syncCommand?.AdminOnlyMessage ?? "Tento příkaz je pouze pro administrátory!";
            var successMsg = syncCommand?.SuccessMessage ?? "✓ Reakce na board zprávě byly aktualizovány.";
            var errorMsg = syncCommand?.ErrorMessage ?? "Chyba: {0}";

            var guildUser = command.User as SocketGuildUser;
            if (!guildUser?.GuildPermissions.Administrator ?? true && command.User.Id != guild.OwnerId)
            {
                await command.FollowupAsync(adminOnlyMsg, ephemeral: true);
                await LogCommandUsageAsync(syncCommand, command.User, false, "Permission denied", context);
                return;
            }

            try
            {
                var botConfig = await context.BotDataService.GetAsync(context.Bot.BotId);
                if (!ulong.TryParse(botConfig.BoardChannelId, out var boardChannelId))
                {
                    await command.FollowupAsync("Board channel is not configured.", ephemeral: true);
                    await LogCommandUsageAsync(syncCommand, command.User, false, "Board channel missing", context);
                    return;
                }

                var boardChannel = guild.GetTextChannel(boardChannelId);
                if (boardChannel == null)
                {
                    await command.FollowupAsync("Configured board channel was not found.", ephemeral: true);
                    await LogCommandUsageAsync(syncCommand, command.User, false, "Board channel not found", context);
                    return;
                }

                IUserMessage? boardMessage = null;
                if (ulong.TryParse(botConfig.BoardMessageId, out var boardMessageId))
                {
                    boardMessage = await boardChannel.GetMessageAsync(boardMessageId) as IUserMessage;
                }

                if (boardMessage == null)
                {
                    boardMessage = await FindBoardMessageAsync(boardChannel);
                }

                if (boardMessage == null)
                {
                    await command.FollowupAsync("Board message was not found. Run /board insert first.", ephemeral: true);
                    await LogCommandUsageAsync(syncCommand, command.User, false, "Board message not found", context);
                    return;
                }

                var teamsData = await context.TeamsDataService.GetAsync(context.Bot.BotId);
                if (!teamsData.Teams.Any())
                {
                    await command.FollowupAsync("No teams are configured, so no reactions were added.", ephemeral: true);
                    await LogCommandUsageAsync(syncCommand, command.User, true, null, context);
                    return;
                }

                try
                {
                    await boardMessage.RemoveAllReactionsAsync();
                }
                catch (Exception ex)
                {
                    context.Logger.LogWarning(ex, "Could not clear existing reactions from board message {MessageId}", boardMessage.Id);
                }

                var addedCount = 0;
                foreach (var team in teamsData.Teams)
                {
                    var emoji = string.IsNullOrWhiteSpace(team.Emoji) ? "🎯" : team.Emoji;
                    if (Emoji.TryParse(emoji, out var parsedEmoji))
                    {
                        await boardMessage.AddReactionAsync(parsedEmoji);
                        addedCount++;
                    }
                }

                await command.FollowupAsync($"{successMsg} Added {addedCount} reactions.", ephemeral: true);
                await LogCommandUsageAsync(syncCommand, command.User, true, null, context);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Error syncing board reactions");
                await command.FollowupAsync(string.Format(errorMsg, ex.Message), ephemeral: true);
                await LogCommandUsageAsync(syncCommand, command.User, false, ex.Message, context);
            }
        }

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
                var botConfig = await context.BotDataService.GetAsync(context.Bot.BotId);
                if (!ulong.TryParse(botConfig.BoardMessageId, out var boardMessageId) || reaction.MessageId != boardMessageId)
                {
                    return;
                }

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
                var botConfig = await context.BotDataService.GetAsync(context.Bot.BotId);
                if (!ulong.TryParse(botConfig.BoardMessageId, out var boardMessageId) || reaction.MessageId != boardMessageId)
                {
                    return;
                }

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

        private static async Task<IUserMessage?> FindBoardMessageAsync(SocketTextChannel channel)
        {
            var messages = await channel.GetMessagesAsync(limit: 100).FlattenAsync();
            return messages
                .OfType<IUserMessage>()
                .FirstOrDefault(m => string.Equals(m.Content, BoardMessageMarker, StringComparison.Ordinal));
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
