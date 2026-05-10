using Discord;
using Discord.WebSocket;
using BotManager.Api.Services;
using BotManager.Api.Models;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Api.BotPlugins.DiscordBotAlliancePlugin.Handlers
{
    /// <summary>
    /// Handles team-related commands: /pridat-tym, /odebrat-tym, /upravit-tym, /seznam-tymu
    /// </summary>
    public class TeamsCommandHandler
    {
        private const string BoardMessageMarker = "[DBA_BOARD]";

        /// <summary>
        /// Handle /pridat-tym command - add a new team
        /// </summary>
        public async Task HandleAddTeamAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, PluginContext context)
        {
            var teamName = command.Data.Options.FirstOrDefault(o => o.Name == "nazev")?.Value as string ?? "";
            var leaderName = command.Data.Options.FirstOrDefault(o => o.Name == "velitel")?.Value as string ?? "";
            var contact = command.Data.Options.FirstOrDefault(o => o.Name == "kontakt")?.Value as string ?? "";

            // Get command definition from database for localized strings
            var teamCommand = await GetCommandAsync("pridat-tym", context);
            var userHint = teamCommand?.UserHint ?? "Zadej název týmu, jméno velitele a kontakt.";
            var successMsg = teamCommand?.SuccessMessage ?? "✓ Tým '{0}' byl úspěšně přidán!";
            var errorMsg = teamCommand?.ErrorMessage ?? "Chyba: {0}";

            if (string.IsNullOrWhiteSpace(teamName) || string.IsNullOrWhiteSpace(leaderName))
            {
                await command.FollowupAsync(userHint, ephemeral: true);
                await LogCommandUsageAsync(teamCommand, command.User, false, "Missing required parameters", context);
                return;
            }

            try
            {
                // Load current teams
                var teamsData = await context.TeamsDataService.GetAsync(context.Bot.BotId);
                var existingTeam = teamsData.Teams.FirstOrDefault(t => t.Name == teamName);

                if (existingTeam != null)
                {
                    var conflictMsg = teamCommand?.InvalidArgumentsMessage ?? $"Tým '{teamName}' již existuje!";
                    await command.FollowupAsync(conflictMsg, ephemeral: true);
                    await LogCommandUsageAsync(teamCommand, command.User, false, "Team already exists", context);
                    return;
                }

                // Create role in Discord with random color
                var randomColor = new Color((uint)Random.Shared.Next(0x1000000));
                var role = await guild.CreateRoleAsync(teamName, color: randomColor);

                // Create new team entity
                var newTeam = new TeamDto
                {
                    Name = teamName,
                    LeaderName = leaderName,
                    Contact = contact,
                    Emoji = "🎯" // Default emoji, will be customized later
                };

                // Add team to storage
                teamsData.Teams.Add(newTeam);
                await context.TeamsDataService.SaveAsync(context.Bot.BotId, teamsData);

                // Log success
                context.Logger.LogInformation("Team '{TeamName}' created successfully with role {RoleId}", teamName, role.Id);
                await command.FollowupAsync(string.Format(successMsg, teamName), ephemeral: true);
                await LogCommandUsageAsync(teamCommand, command.User, true, null, context);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Error adding team");
                await command.FollowupAsync(string.Format(errorMsg, ex.Message), ephemeral: true);
                await LogCommandUsageAsync(teamCommand, command.User, false, ex.Message, context);
            }
        }

        /// <summary>
        /// Handle /odebrat-tym command - remove a team
        /// </summary>
        public async Task HandleRemoveTeamAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, PluginContext context)
        {
            var teamName = command.Data.Options.FirstOrDefault(o => o.Name == "nazev")?.Value as string ?? "";

            // Get command definition from database for localized strings
            var teamCommand = await GetCommandAsync("odebrat-tym", context);
            var userHint = teamCommand?.UserHint ?? "Zadej název týmu ke smazání.";
            var successMsg = teamCommand?.SuccessMessage ?? "✓ Tým '{0}' byl úspěšně odstraněn!";
            var errorMsg = teamCommand?.ErrorMessage ?? "Chyba: {0}";

            if (string.IsNullOrWhiteSpace(teamName))
            {
                await command.FollowupAsync(userHint, ephemeral: true);
                await LogCommandUsageAsync(teamCommand, command.User, false, "Missing team name", context);
                return;
            }

            try
            {
                // Load current teams
                var teamsData = await context.TeamsDataService.GetAsync(context.Bot.BotId);
                var teamToRemove = teamsData.Teams.FirstOrDefault(t => t.Name == teamName);

                if (teamToRemove == null)
                {
                    var notFoundMsg = teamCommand?.InvalidArgumentsMessage ?? $"Tým '{teamName}' nebyl nalezen!";
                    await command.FollowupAsync(notFoundMsg, ephemeral: true);
                    await LogCommandUsageAsync(teamCommand, command.User, false, "Team not found", context);
                    return;
                }

                // Find and delete role from Discord
                var role = guild.Roles.FirstOrDefault(r => r.Name == teamName);
                if (role != null)
                {
                    await role.DeleteAsync();
                    context.Logger.LogInformation("Deleted role {RoleId} for team '{TeamName}'", role.Id, teamName);
                }

                // Remove team from storage
                teamsData.Teams.Remove(teamToRemove);
                await context.TeamsDataService.SaveAsync(context.Bot.BotId, teamsData);

                context.Logger.LogInformation("Team '{TeamName}' removed successfully", teamName);
                await command.FollowupAsync(string.Format(successMsg, teamName), ephemeral: true);
                await LogCommandUsageAsync(teamCommand, command.User, true, null, context);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Error removing team");
                await command.FollowupAsync(string.Format(errorMsg, ex.Message), ephemeral: true);
                await LogCommandUsageAsync(teamCommand, command.User, false, ex.Message, context);
            }
        }

        /// <summary>
        /// Handle /upravit-tym command - edit a team
        /// </summary>
        public async Task HandleEditTeamAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, PluginContext context)
        {
            var teamName = command.Data.Options.FirstOrDefault(o => o.Name == "nazev")?.Value as string ?? "";
            var newName = command.Data.Options.FirstOrDefault(o => o.Name == "novy-nazev")?.Value as string;
            var newLeader = command.Data.Options.FirstOrDefault(o => o.Name == "novy-velitel")?.Value as string;
            var newContact = command.Data.Options.FirstOrDefault(o => o.Name == "novy-kontakt")?.Value as string;

            // Get command definition from database for localized strings
            var teamCommand = await GetCommandAsync("upravit-tym", context);
            var userHint = teamCommand?.UserHint ?? "Zadej název týmu a nová data.";
            var successMsg = teamCommand?.SuccessMessage ?? "✓ Tým '{0}' byl upraven!";
            var errorMsg = teamCommand?.ErrorMessage ?? "Chyba: {0}";

            if (string.IsNullOrWhiteSpace(teamName))
            {
                await command.FollowupAsync(userHint, ephemeral: true);
                await LogCommandUsageAsync(teamCommand, command.User, false, "Missing team name", context);
                return;
            }

            try
            {
                // Load current teams
                var teamsData = await context.TeamsDataService.GetAsync(context.Bot.BotId);
                var teamToEdit = teamsData.Teams.FirstOrDefault(t => t.Name == teamName);

                if (teamToEdit == null)
                {
                    var notFoundMsg = teamCommand?.InvalidArgumentsMessage ?? $"Tým '{teamName}' nebyl nalezen!";
                    await command.FollowupAsync(notFoundMsg, ephemeral: true);
                    await LogCommandUsageAsync(teamCommand, command.User, false, "Team not found", context);
                    return;
                }

                // Update team properties if provided
                if (!string.IsNullOrWhiteSpace(newName) && newName != teamName)
                {
                    // Check if new name conflicts with existing team
                    if (teamsData.Teams.Any(t => t.Name == newName && t.Name != teamName))
                    {
                        var conflictMsg = teamCommand?.InvalidArgumentsMessage ?? $"Tým se jménem '{newName}' již existuje!";
                        await command.FollowupAsync(conflictMsg, ephemeral: true);
                        await LogCommandUsageAsync(teamCommand, command.User, false, "New team name conflicts", context);
                        return;
                    }

                    // Rename role if it exists
                    var role = guild.Roles.FirstOrDefault(r => r.Name == teamName);
                    if (role != null)
                    {
                        await role.ModifyAsync(props => props.Name = newName);
                        context.Logger.LogInformation("Renamed role {RoleId} from '{OldName}' to '{NewName}'", role.Id, teamName, newName);
                    }

                    teamToEdit.Name = newName;
                }

                if (!string.IsNullOrWhiteSpace(newLeader))
                {
                    teamToEdit.LeaderName = newLeader;
                }

                if (!string.IsNullOrWhiteSpace(newContact))
                {
                    teamToEdit.Contact = newContact;
                }

                // Save changes
                await context.TeamsDataService.SaveAsync(context.Bot.BotId, teamsData);

                context.Logger.LogInformation("Team '{TeamName}' modified successfully", teamName);
                await command.FollowupAsync(string.Format(successMsg, teamName), ephemeral: true);
                await LogCommandUsageAsync(teamCommand, command.User, true, null, context);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Error editing team");
                await command.FollowupAsync(string.Format(errorMsg, ex.Message), ephemeral: true);
                await LogCommandUsageAsync(teamCommand, command.User, false, ex.Message, context);
            }
        }

        /// <summary>
        /// Handle /seznam-tymu command - show all teams
        /// </summary>
        public async Task HandleShowTeamsListAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, PluginContext context)
        {
            // Get command definition from database for localized strings
            var teamCommand = await GetCommandAsync("seznam-tymu", context);
            var adminOnlyMsg = teamCommand?.AdminOnlyMessage ?? "Tento příkaz je pouze pro administrátory!";
            var errorMsg = teamCommand?.ErrorMessage ?? "Chyba: {0}";

            // Check if user is admin (admin role or guild owner)
            var guildUser = command.User as SocketGuildUser;
            if (!guildUser?.GuildPermissions.Administrator ?? true && command.User.Id != guild.OwnerId)
            {
                await command.FollowupAsync(adminOnlyMsg, ephemeral: true);
                await LogCommandUsageAsync(teamCommand, command.User, false, "Permission denied", context);
                return;
            }

            try
            {
                // Load teams
                var teamsData = await context.TeamsDataService.GetAsync(context.Bot.BotId);

                var botConfig = await context.DbContext.BotConfigurations
                    .FirstOrDefaultAsync(c => c.BotId == context.Bot.BotId);

                if (botConfig?.BoardChannelId == null)
                {
                    await command.FollowupAsync("BoardChannelId není nastaven v konfiguraci bota.", ephemeral: true);
                    await LogCommandUsageAsync(teamCommand, command.User, false, "Missing BoardChannelId", context);
                    return;
                }

                var boardChannel = guild.GetTextChannel(botConfig.BoardChannelId.Value);
                if (boardChannel == null)
                {
                    await command.FollowupAsync($"Kanál s ID {botConfig.BoardChannelId.Value} nebyl nalezen na serveru.", ephemeral: true);
                    await LogCommandUsageAsync(teamCommand, command.User, false, "Board channel not found", context);
                    return;
                }

                if (!teamsData.Teams.Any())
                {
                    var emptyMsg = teamCommand?.SuccessMessage ?? "Nejsou registrovány žádné týmy.";
                    await command.FollowupAsync(emptyMsg, ephemeral: true);
                    await LogCommandUsageAsync(teamCommand, command.User, true, null, context);
                    return;
                }

                // Create embed with teams list
                var embed = new EmbedBuilder()
                    .WithTitle("📋 Seznam všech týmů")
                    .WithColor(Color.Blue)
                    .WithFooter($"Aktualizováno: {DateTime.Now:dd.MM.yyyy HH:mm:ss}")
                    .WithDescription($"Celkem registrovaných týmů: {teamsData.Teams.Count}");

                foreach (var team in teamsData.Teams)
                {
                    var emoji = string.IsNullOrEmpty(team.Emoji) ? "🎯" : team.Emoji;
                    var fieldName = $"{emoji} {team.Name}";
                    var fieldValue = $"**Velitel:** {team.LeaderName}\n**Kontakt:** {team.Contact}";
                    
                    embed.AddField(fieldName, fieldValue, inline: false);
                }

                var shouldDiscover = !botConfig.BoardMessageId.HasValue;
                if (shouldDiscover)
                {
                    botConfig.BoardMessageId = await FindBotBoardMessageAsync(boardChannel, guild.CurrentUser.Id);
                    if (botConfig.BoardMessageId.HasValue)
                    {
                        await context.DbContext.SaveChangesAsync();
                    }
                }

                IUserMessage? targetMessage = null;
                if (botConfig.BoardMessageId.HasValue)
                {
                    targetMessage = await boardChannel.GetMessageAsync(botConfig.BoardMessageId.Value) as IUserMessage;
                }

                if (targetMessage != null)
                {
                    await targetMessage.ModifyAsync(m => m.Content = BoardMessageMarker);
                    await targetMessage.ModifyAsync(m => m.Embed = embed.Build());
                }
                else
                {
                    var newMessage = await boardChannel.SendMessageAsync(BoardMessageMarker, embed: embed.Build());
                    botConfig.BoardMessageId = newMessage.Id;
                    await context.DbContext.SaveChangesAsync();
                }

                await command.FollowupAsync($"Seznam týmů byl publikován do kanálu <#{boardChannel.Id}>.", ephemeral: true);
                context.Logger.LogInformation("Teams list displayed, total: {TeamCount}", teamsData.Teams.Count);
                await LogCommandUsageAsync(teamCommand, command.User, true, null, context);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Error showing teams list");
                await command.FollowupAsync(string.Format(errorMsg, ex.Message), ephemeral: true);
                await LogCommandUsageAsync(teamCommand, command.User, false, ex.Message, context);
            }
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

        private static async Task<ulong?> FindBotBoardMessageAsync(SocketTextChannel channel, ulong botUserId)
        {
            var messages = await channel.GetMessagesAsync(limit: 100).FlattenAsync();
            var byMarker = messages
                .OfType<IUserMessage>()
                .FirstOrDefault(m => m.Author.Id == botUserId && string.Equals(m.Content, BoardMessageMarker, StringComparison.Ordinal));

            if (byMarker != null)
            {
                return byMarker.Id;
            }

            // Backward compatibility for older board posts before marker was introduced.
            var fallback = messages
                .OfType<IUserMessage>()
                .FirstOrDefault(m => m.Author.Id == botUserId && m.Embeds.Count > 0);

            return fallback?.Id;
        }
    }
}
