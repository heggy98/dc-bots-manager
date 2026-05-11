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
    /// Handles team-related commands: /add-team, /remove-team, /edit-team, /list-teams, /publish-board
    /// </summary>
    public class TeamsCommandHandler
    {
        /// <summary>
        /// Handle /add-team command - add a new team
        /// </summary>
        public async Task HandleAddTeamAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, PluginContext context)
        {
            var teamName = GetStringOption(command, "name") ?? "";
            var leaderName = GetStringOption(command, "leader") ?? "";
            var contact = GetStringOption(command, "contact") ?? "";

            // Get command definition from database for localized strings
            var teamCommand = await GetCommandAsync("add-team", context);
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
                await RefreshBoardMessageAsync(context, teamsData);

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
        /// Handle /remove-team command - remove a team
        /// </summary>
        public async Task HandleRemoveTeamAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, PluginContext context)
        {
            var teamName = GetStringOption(command, "name") ?? "";

            // Get command definition from database for localized strings
            var teamCommand = await GetCommandAsync("remove-team", context);
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
                await RefreshBoardMessageAsync(context, teamsData);

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
        /// Handle /edit-team command - edit a team
        /// </summary>
        public async Task HandleEditTeamAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, PluginContext context)
        {
            var teamName = GetStringOption(command, "name") ?? "";
            var newName = GetStringOption(command, "new-name");
            var newLeader = GetStringOption(command, "new-leader");
            var newContact = GetStringOption(command, "new-contact");

            // Get command definition from database for localized strings
            var teamCommand = await GetCommandAsync("edit-team", context);
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
                await RefreshBoardMessageAsync(context, teamsData);

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
        /// Handle /list-teams command - show all teams
        /// </summary>
        public async Task HandleShowTeamsListAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, PluginContext context)
        {
            // Get command definition from database for localized strings
            var teamCommand = await GetCommandAsync("list-teams", context);
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

                if (!teamsData.Teams.Any())
                {
                    var emptyMsg = teamCommand?.SuccessMessage ?? "Nejsou registrovány žádné týmy.";
                    await command.FollowupAsync(emptyMsg, ephemeral: true);
                    await LogCommandUsageAsync(teamCommand, command.User, true, null, context);
                    return;
                }

                var boardUpdated = await RefreshBoardMessageAsync(context, teamsData);

                await command.FollowupAsync(boardUpdated
                    ? "Seznam týmů byl aktualizován na board zprávě."
                    : "Týmy byly načteny, ale board zprávu se nepodařilo aktualizovat.", ephemeral: true);
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

        public async Task HandlePublishBoardAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, PluginContext context)
        {
            var boardCommand = await GetCommandAsync("publish-board", context);
            var type = GetStringOption(command, "type") ?? "teams";
            var customTitle = GetStringOption(command, "title");
            var customDescription = GetStringOption(command, "description");

            try
            {
                if (string.Equals(type, "plain", StringComparison.OrdinalIgnoreCase))
                {
                    var discordBotService = context.ServiceProvider.GetService(typeof(IDiscordBotService)) as IDiscordBotService;
                    if (discordBotService == null)
                    {
                        await command.FollowupAsync("Discord bot service is not available.", ephemeral: true);
                        await LogCommandUsageAsync(boardCommand, command.User, false, "Discord bot service unavailable", context);
                        return;
                    }

                    var plainBoard = new BotManager.Backend.Bots.Models.BoardMessageDto
                    {
                        Title = string.IsNullOrWhiteSpace(customTitle) ? "📋 Board" : customTitle,
                        Description = string.IsNullOrWhiteSpace(customDescription)
                            ? "Board published manually."
                            : customDescription,
                        Entries = new List<BotManager.Backend.Bots.Models.BoardMessageEntryDto>()
                    };

                    var updated = await discordBotService.RefreshBoardMessageAsync(context.Bot.BotId, plainBoard);
                    await command.FollowupAsync(updated
                        ? "Plain board was published."
                        : "Failed to publish plain board.", ephemeral: true);
                    await LogCommandUsageAsync(boardCommand, command.User, updated, updated ? null : "Board update failed", context);
                    return;
                }

                var teamsData = await context.TeamsDataService.GetAsync(context.Bot.BotId);
                var teamsBoard = BoardMessageFactory.FromTeams(teamsData);

                if (!string.IsNullOrWhiteSpace(customTitle))
                {
                    teamsBoard.Title = customTitle;
                }

                if (!string.IsNullOrWhiteSpace(customDescription))
                {
                    teamsBoard.Description = customDescription;
                }

                var service = context.ServiceProvider.GetService(typeof(IDiscordBotService)) as IDiscordBotService;
                if (service == null)
                {
                    await command.FollowupAsync("Discord bot service is not available.", ephemeral: true);
                    await LogCommandUsageAsync(boardCommand, command.User, false, "Discord bot service unavailable", context);
                    return;
                }

                var boardUpdated = await service.RefreshBoardMessageAsync(context.Bot.BotId, teamsBoard);
                await command.FollowupAsync(boardUpdated
                    ? "Team board was published."
                    : "Failed to publish team board.", ephemeral: true);
                await LogCommandUsageAsync(boardCommand, command.User, boardUpdated, boardUpdated ? null : "Board update failed", context);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Error publishing board");
                await command.FollowupAsync($"Error publishing board: {ex.Message}", ephemeral: true);
                await LogCommandUsageAsync(boardCommand, command.User, false, ex.Message, context);
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

        private async Task<bool> RefreshBoardMessageAsync(PluginContext context, BotTeamsDto teamsData)
        {
            var discordBotService = context.ServiceProvider.GetService(typeof(IDiscordBotService)) as IDiscordBotService;
            if (discordBotService == null)
            {
                context.Logger.LogWarning("Discord bot service is not available, skipping board refresh");
                return false;
            }

            return await discordBotService.RefreshBoardMessageAsync(context.Bot.BotId, BoardMessageFactory.FromTeams(teamsData));
        }

        private static string? GetStringOption(SocketSlashCommand command, string optionName)
        {
            return command.Data.Options.FirstOrDefault(o => o.Name == optionName)?.Value as string;
        }
    }
}
