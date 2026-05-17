using Discord;
using Discord.WebSocket;
using BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Services;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Shared.Models;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Handlers
{
    /// <summary>
    /// Handles reaction-related commands: /move-reactions
    /// </summary>
    public class ReactionsCommandHandler : BoardCommandHandlerBase
    {
        private const string BoardMessageMarker = "\u200B";
        private readonly IBoardMessageLocator _boardMessageLocator;

        public ReactionsCommandHandler(
            IBoardCommandAuditService boardCommandAuditService,
            IBoardMessageLocator boardMessageLocator)
            : base(boardCommandAuditService)
        {
            _boardMessageLocator = boardMessageLocator;
        }

        /// <summary>
        /// Handle /board sync-reactions command - regenerate board message reactions from team emojis
        /// </summary>
        public async Task HandleSyncBoardReactionsAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, IPluginContext context)
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
                    boardMessage = await _boardMessageLocator.FindBoardMessageAsync(boardChannel, BoardMessageMarker);
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

                var currentTeamNames = teamsData.Teams
                    .Select(team => team.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToHashSet(StringComparer.Ordinal);

                var previousBoardRoleNames = ExtractBoardRoleNames(boardMessage);
                var (rolesCreated, rolesRemoved) = await SyncBoardRolesAsync(
                    guild,
                    currentTeamNames,
                    previousBoardRoleNames,
                    context);

                var clearedReactions = false;
                try
                {
                    await boardMessage.RemoveAllReactionsAsync();
                    clearedReactions = true;
                }
                catch (Exception ex)
                {
                    context.Logger.LogWarning(ex, "Could not clear existing reactions from board message {MessageId} for BotId={BotId}. Missing Manage Messages permission?", boardMessage.Id, context.Bot.BotId);
                }

                var existingEmojiStrings = clearedReactions
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : boardMessage.Reactions.Keys
                        .Select(emote => emote.ToString())
                        .Where(emote => !string.IsNullOrWhiteSpace(emote))
                        .Select(emote => emote!)
                        .ToHashSet(StringComparer.Ordinal);

                var addedCount = 0;
                foreach (var team in teamsData.Teams)
                {
                    var emoji = string.IsNullOrWhiteSpace(team.Emoji) ? "🎯" : team.Emoji;
                    if (Emoji.TryParse(emoji, out var parsedEmoji))
                    {
                        var emojiKey = parsedEmoji.ToString();
                        if (existingEmojiStrings.Contains(emojiKey))
                        {
                            continue;
                        }

                        await boardMessage.AddReactionAsync(parsedEmoji);
                        existingEmojiStrings.Add(emojiKey);
                        addedCount++;
                    }
                }

                var suffix = clearedReactions
                    ? string.Empty
                    : " Existing reactions could not be removed; only missing reactions were added.";
                await command.FollowupAsync(
                    $"{successMsg} Added {addedCount} reactions. Created {rolesCreated} roles. Removed {rolesRemoved} stale board roles.{suffix}",
                    ephemeral: true);
                await LogCommandUsageAsync(syncCommand, command.User, true, null, context);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "BotId={BotId}: Error syncing board reactions", context.Bot.BotId);
                await command.FollowupAsync(string.Format(errorMsg, ex.Message), ephemeral: true);
                await LogCommandUsageAsync(syncCommand, command.User, false, ex.Message, context);
            }
        }

        /// <summary>
        /// Handle /move-reactions command - move reaction role message to another channel
        /// </summary>
        public async Task HandleMoveReactionMessageAsync(SocketSlashCommand command, SocketGuild guild, IPluginContext context)
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
                        context.Logger.LogWarning(ex, "BotId={BotId}: Failed to add reaction emoji '{Emoji}' to message", context.Bot.BotId, emoji);
                    }
                }

                context.Logger.LogInformation("BotId={BotId}: Moved role selection message to channel {ChannelId}", context.Bot.BotId, targetChannelValue.Id);
                await command.FollowupAsync(string.Format(successMsg), ephemeral: true);
                await LogCommandUsageAsync(reactionCommand, command.User, true, null, context);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "BotId={BotId}: Error moving reaction message", context.Bot.BotId);
                await command.FollowupAsync(string.Format(errorMsg, ex.Message), ephemeral: true);
                await LogCommandUsageAsync(reactionCommand, command.User, false, ex.Message, context);
            }
        }

        /// <summary>
        /// Handle reaction added event - assign role when user reacts
        /// </summary>
        public async Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> cachedMessage, ISocketMessageChannel channel,
            SocketReaction reaction, SocketGuild guild, SocketGuildUser user, IPluginContext context)
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
                    context.Logger.LogInformation("BotId={BotId}: Created role '{RoleName}' for team", context.Bot.BotId, team.Name);
                    // Try to get the role from the guild cache
                    role = guild.Roles.FirstOrDefault(r => r.Name == team.Name);
                }

                if (role != null)
                {
                    // Assign role to user
                    await user.AddRoleAsync(role);
                    var userTag = BuildUserTag(user);
                    context.Logger.LogInformation("BotId={BotId}: Assigned role '{RoleName}' to user {UserTag}", context.Bot.BotId, team.Name, userTag);

                    var commandService = context.ServiceProvider.GetService(typeof(CommandManagementService)) as CommandManagementService;
                    if (commandService != null)
                    {
                        var reactionCommand = await commandService.GetCommandAsync(context.Bot.BotId, "reaction-assign");
                        if (reactionCommand != null)
                        {
                            await commandService.LogCommandUsageAsync(reactionCommand.CommandId, user.Id, userTag, isSuccess: true,
                                errorMessage: null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "BotId={BotId}: Error handling reaction added event", context.Bot.BotId);
            }
        }

        /// <summary>
        /// Handle reaction removed event - remove role when user removes reaction
        /// </summary>
        public async Task HandleReactionRemovedAsync(Cacheable<IUserMessage, ulong> cachedMessage, ISocketMessageChannel channel,
            SocketReaction reaction, SocketGuild guild, SocketGuildUser user, IPluginContext context)
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
                var userTag = BuildUserTag(user);
                context.Logger.LogInformation("BotId={BotId}: Removed role '{RoleName}' from user {UserTag}", context.Bot.BotId, team.Name, userTag);

                var commandService = context.ServiceProvider.GetService(typeof(CommandManagementService)) as CommandManagementService;
                if (commandService != null)
                {
                    var reactionCommand = await commandService.GetCommandAsync(context.Bot.BotId, "reaction-unassign");
                    if (reactionCommand != null)
                    {
                        await commandService.LogCommandUsageAsync(reactionCommand.CommandId, user.Id, userTag, isSuccess: true,
                            errorMessage: null);
                    }
                }
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "BotId={BotId}: Error handling reaction removed event", context.Bot.BotId);
            }
        }

        /// <summary>
        /// Find team by emoji
        /// </summary>
        private Task<Team?> FindTeamByEmojiAsync(string emojiName, IPluginContext context)
        {
            return Task.FromResult<Team?>(null); // Placeholder
        }

        /// <summary>
        /// Extracts role names currently represented on the board embed message.
        /// </summary>
        private static HashSet<string> ExtractBoardRoleNames(IUserMessage boardMessage)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var embedFields = boardMessage.Embeds.SelectMany(embed => embed.Fields);

            foreach (var field in embedFields)
            {
                var roleName = ExtractRoleNameFromField(field.Name);
                if (!string.IsNullOrWhiteSpace(roleName))
                {
                    names.Add(roleName);
                }
            }

            return names;
        }

        /// <summary>
        /// Creates missing current board roles and removes stale board-managed roles.
        /// </summary>
        private static async Task<(int Created, int Removed)> SyncBoardRolesAsync(
            SocketGuild guild,
            HashSet<string> currentTeamNames,
            HashSet<string> previousBoardRoleNames,
            IPluginContext context)
        {
            var created = 0;
            var removed = 0;

            foreach (var teamName in currentTeamNames)
            {
                var exists = guild.Roles.Any(role => string.Equals(role.Name, teamName, StringComparison.Ordinal));
                if (exists)
                {
                    continue;
                }

                try
                {
                    var randomColor = new Color((uint)Random.Shared.Next(0x1000000));
                    await guild.CreateRoleAsync(teamName, color: randomColor);
                    created++;
                    context.Logger.LogInformation("BotId={BotId}: Created missing board role '{RoleName}' during sync-reactions", context.Bot.BotId, teamName);
                }
                catch (Exception ex)
                {
                    context.Logger.LogWarning(ex, "BotId={BotId}: Failed to create missing board role '{RoleName}' during sync-reactions", context.Bot.BotId, teamName);
                }
            }

            var staleBoardRoles = previousBoardRoleNames
                .Where(previousRole => !currentTeamNames.Contains(previousRole))
                .ToList();

            foreach (var staleRoleName in staleBoardRoles)
            {
                var role = guild.Roles.FirstOrDefault(r => string.Equals(r.Name, staleRoleName, StringComparison.Ordinal));
                if (role == null)
                {
                    continue;
                }

                try
                {
                    await role.DeleteAsync();
                    removed++;
                    context.Logger.LogInformation("BotId={BotId}: Removed stale board role '{RoleName}' during sync-reactions", context.Bot.BotId, staleRoleName);
                }
                catch (Exception ex)
                {
                    context.Logger.LogWarning(ex, "BotId={BotId}: Failed to remove stale board role '{RoleName}' during sync-reactions", context.Bot.BotId, staleRoleName);
                }
            }

            return (created, removed);
        }

        /// <summary>
        /// Extracts team role name from board embed field name format.
        /// </summary>
        private static string ExtractRoleNameFromField(string? fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                return string.Empty;
            }

            var trimmed = fieldName.Trim();
            var firstSpace = trimmed.IndexOf(' ');
            if (firstSpace <= 0)
            {
                return trimmed;
            }

            var firstToken = trimmed[..firstSpace];
            var appearsToBeEmoji = firstToken.Any(ch => char.IsSymbol(ch) || char.IsSurrogate(ch) || ch == ':' || ch == '<' || ch == '>');
            return appearsToBeEmoji ? trimmed[(firstSpace + 1)..].Trim() : trimmed;
        }

        /// <summary>
        /// Builds a readable user tag for logs from available Discord name fields.
        /// </summary>
        private static string BuildUserTag(SocketGuildUser user)
        {
            if (!string.IsNullOrWhiteSpace(user.GlobalName))
            {
                return $"{user.GlobalName} (@{user.Username})";
            }

            if (!string.IsNullOrWhiteSpace(user.Discriminator) && user.Discriminator != "0000")
            {
                return $"{user.Username}#{user.Discriminator}";
            }

            return user.Username;
        }
    }
}
