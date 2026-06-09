using Discord;
using Discord.WebSocket;
using BotManager.Backend.API.Services;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Services;
using BotManager.Backend.Shared.Models;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Handlers
{
    /// <summary>
    /// Handles team-related commands: /board add-team, /board remove-team, /board edit-team, /board refresh, /board insert
    /// </summary>
    public class TeamsCommandHandler : BoardCommandHandlerBase
    {
        private readonly IEmojiCatalogService _emojiCatalogService;

        public TeamsCommandHandler(
            IBoardCommandAuditService boardCommandAuditService,
            IEmojiCatalogService emojiCatalogService)
            : base(boardCommandAuditService)
        {
            _emojiCatalogService = emojiCatalogService;
        }

        /// <summary>
        /// Handle /add-team command - add a new team
        /// </summary>
        public async Task HandleAddTeamAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, IPluginContext context)
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
                await SendCommandResponseAsync(command, userHint, context);
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
                    await SendCommandResponseAsync(command, conflictMsg, context);
                    await LogCommandUsageAsync(teamCommand, command.User, false, "Team already exists", context);
                    return;
                }

                var usedEmojis = teamsData.Teams
                    .Select(t => string.IsNullOrWhiteSpace(t.Emoji) ? "🎯" : t.Emoji.Trim())
                    .ToHashSet(StringComparer.Ordinal);

                var catalog = await _emojiCatalogService.GetEmojiCatalogAsync();
                var availableEmojis = catalog
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Select(e => e.Trim())
                    .Where(e => !usedEmojis.Contains(e))
                    .ToList();

                if (availableEmojis.Count == 0)
                {
                    await SendCommandResponseAsync(command, "No unique emojis are available for another team on this board.", context);
                    await LogCommandUsageAsync(teamCommand, command.User, false, "No unique emojis available", context);
                    return;
                }

                var randomEmoji = availableEmojis[Random.Shared.Next(availableEmojis.Count)];

                // Create role in Discord with random color
                var randomColor = new Color((uint)Random.Shared.Next(0x1000000));
                var role = await guild.CreateRoleAsync(teamName, color: randomColor);

                // Create new team entity
                var newTeam = new TeamDto
                {
                    Name = teamName,
                    LeaderName = leaderName,
                    Contact = contact,
                    Emoji = randomEmoji
                };

                // Add team to storage
                teamsData.Teams.Add(newTeam);
                await context.TeamsDataService.SaveAsync(context.Bot.BotId, teamsData);
                await RefreshBoardMessageAsync(context, teamsData);
                await SyncBoardReactionsIfPresentAsync(context, teamsData);

                // Log success
                context.Logger.LogInformation("BotId={BotId}: Team '{TeamName}' created successfully with role {RoleId}", context.Bot.BotId, teamName, role.Id);
                await SendCommandResponseAsync(command, string.Format(successMsg, teamName), context);
                await LogCommandUsageAsync(teamCommand, command.User, true, null, context);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "BotId={BotId}: Error adding team", context.Bot.BotId);
                await SendCommandResponseAsync(command, string.Format(errorMsg, ex.Message), context);
                await LogCommandUsageAsync(teamCommand, command.User, false, ex.Message, context);
            }
        }

        /// <summary>
        /// Handle /remove-team command - remove a team
        /// </summary>
        public async Task HandleRemoveTeamAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, IPluginContext context)
        {
            var teamName = GetStringOption(command, "name") ?? "";

            // Get command definition from database for localized strings
            var teamCommand = await GetCommandAsync("remove-team", context);
            var userHint = teamCommand?.UserHint ?? "Zadej název týmu ke smazání.";
            var successMsg = teamCommand?.SuccessMessage ?? "✓ Tým '{0}' byl úspěšně odstraněn!";
            var errorMsg = teamCommand?.ErrorMessage ?? "Chyba: {0}";

            if (string.IsNullOrWhiteSpace(teamName))
            {
                await SendCommandResponseAsync(command, userHint, context);
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
                    await SendCommandResponseAsync(command, notFoundMsg, context);
                    await LogCommandUsageAsync(teamCommand, command.User, false, "Team not found", context);
                    return;
                }

                // Find and delete role from Discord
                var role = guild.Roles.FirstOrDefault(r => r.Name == teamName);
                if (role != null)
                {
                    await role.DeleteAsync();
                    context.Logger.LogInformation("BotId={BotId}: Deleted role {RoleId} for team '{TeamName}'", context.Bot.BotId, role.Id, teamName);
                }

                // Remove team from storage
                teamsData.Teams.Remove(teamToRemove);
                await context.TeamsDataService.SaveAsync(context.Bot.BotId, teamsData);
                await RefreshBoardMessageAsync(context, teamsData);
                await SyncBoardReactionsIfPresentAsync(context, teamsData);

                context.Logger.LogInformation("BotId={BotId}: Team '{TeamName}' removed successfully", context.Bot.BotId, teamName);
                await SendCommandResponseAsync(command, string.Format(successMsg, teamName), context);
                await LogCommandUsageAsync(teamCommand, command.User, true, null, context);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "BotId={BotId}: Error removing team", context.Bot.BotId);
                await SendCommandResponseAsync(command, string.Format(errorMsg, ex.Message), context);
                await LogCommandUsageAsync(teamCommand, command.User, false, ex.Message, context);
            }
        }

        /// <summary>
        /// Handle /edit-team command - edit a team
        /// </summary>
        public async Task HandleEditTeamAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, IPluginContext context)
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
                await SendCommandResponseAsync(command, userHint, context);
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
                    await SendCommandResponseAsync(command, notFoundMsg, context);
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
                        await SendCommandResponseAsync(command, conflictMsg, context);
                        await LogCommandUsageAsync(teamCommand, command.User, false, "New team name conflicts", context);
                        return;
                    }

                    // Rename role if it exists
                    var role = guild.Roles.FirstOrDefault(r => r.Name == teamName);
                    if (role != null)
                    {
                        await role.ModifyAsync(props => props.Name = newName);
                        context.Logger.LogInformation("BotId={BotId}: Renamed role {RoleId} from '{OldName}' to '{NewName}'", context.Bot.BotId, role.Id, teamName, newName);
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

                context.Logger.LogInformation("BotId={BotId}: Team '{TeamName}' modified successfully", context.Bot.BotId, teamName);
                await SendCommandResponseAsync(command, string.Format(successMsg, teamName), context);
                await LogCommandUsageAsync(teamCommand, command.User, true, null, context);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "BotId={BotId}: Error editing team", context.Bot.BotId);
                await SendCommandResponseAsync(command, string.Format(errorMsg, ex.Message), context);
                await LogCommandUsageAsync(teamCommand, command.User, false, ex.Message, context);
            }
        }

        /// <summary>
        /// Handle /board refresh command - refresh team board message
        /// </summary>
        public async Task HandleShowTeamsListAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, IPluginContext context)
        {
            // Get command definition from database for localized strings
            var teamCommand = await GetCommandAsync("refresh", context);
            var adminOnlyMsg = teamCommand?.AdminOnlyMessage ?? "Tento příkaz je pouze pro administrátory!";
            var errorMsg = teamCommand?.ErrorMessage ?? "Chyba: {0}";

            // Check if user is admin (admin role or guild owner)
            var guildUser = command.User as SocketGuildUser;
            if (!guildUser?.GuildPermissions.Administrator ?? true && command.User.Id != guild.OwnerId)
            {
                await SendCommandResponseAsync(command, adminOnlyMsg, context);
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
                    await SendCommandResponseAsync(command, emptyMsg, context);
                    await LogCommandUsageAsync(teamCommand, command.User, true, null, context);
                    return;
                }

                var boardUpdated = await RefreshBoardMessageAsync(context, teamsData);

                await SendCommandResponseAsync(command, boardUpdated
                    ? "Seznam týmů byl aktualizován na board zprávě."
                    : "Týmy byly načteny, ale board zprávu se nepodařilo aktualizovat.", context);
                context.Logger.LogInformation("BotId={BotId}: Teams list displayed, total: {TeamCount}", context.Bot.BotId, teamsData.Teams.Count);
                await LogCommandUsageAsync(teamCommand, command.User, true, null, context);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "BotId={BotId}: Error showing teams list", context.Bot.BotId);
                await SendCommandResponseAsync(command, string.Format(errorMsg, ex.Message), context);
                await LogCommandUsageAsync(teamCommand, command.User, false, ex.Message, context);
            }
        }

        /// <summary>
        /// Inserts or refreshes the board message from currently persisted teams.
        /// </summary>
        public async Task HandleInsertBoardAsync(SocketSlashCommand command, SocketGuild guild, SocketTextChannel channel, IPluginContext context)
        {
            var boardCommand = await GetCommandAsync("insert", context);

            try
            {
                var teamsData = await context.TeamsDataService.GetAsync(context.Bot.BotId);
                var botConfig = await context.BotDataService.GetAsync(context.Bot.BotId);
                var teamsBoard = BoardMessageFactory.FromTeams(teamsData, botConfig);

                var service = context.ServiceProvider.GetService(typeof(IDiscordBotService)) as IDiscordBotService;
                if (service == null)
                {
                    await SendCommandResponseAsync(command, "Discord bot service is not available.", context);
                    await LogCommandUsageAsync(boardCommand, command.User, false, "Discord bot service unavailable", context);
                    return;
                }

                var boardUpdated = await service.RefreshBoardMessageAsync(context.Bot.BotId, teamsBoard);
                await SendCommandResponseAsync(command, boardUpdated
                    ? "Team board was inserted."
                    : "Failed to insert team board.", context);
                await LogCommandUsageAsync(boardCommand, command.User, boardUpdated, boardUpdated ? null : "Board update failed", context);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "BotId={BotId}: Error inserting board", context.Bot.BotId);
                await SendCommandResponseAsync(command, $"Error inserting board: {ex.Message}", context);
                await LogCommandUsageAsync(boardCommand, command.User, false, ex.Message, context);
            }
        }

        /// <summary>
        /// Attempts to refresh the board message using the runtime Discord bot service.
        /// </summary>
        private async Task<bool> RefreshBoardMessageAsync(IPluginContext context, BotTeamsDto teamsData)
        {
            var discordBotService = context.ServiceProvider.GetService(typeof(IDiscordBotService)) as IDiscordBotService;
            if (discordBotService == null)
            {
                context.Logger.LogWarning("BotId={BotId}: Discord bot service is not available, skipping board refresh", context.Bot.BotId);
                return false;
            }

            var botConfig = await context.BotDataService.GetAsync(context.Bot.BotId);
            return await discordBotService.RefreshBoardMessageAsync(context.Bot.BotId, BoardMessageFactory.FromTeams(teamsData, botConfig));
        }

        /// <summary>
        /// Rebuilds board reactions when the board message already contains reactions.
        /// </summary>
        private async Task SyncBoardReactionsIfPresentAsync(IPluginContext context, BotTeamsDto teamsData)
        {
            var discordBotService = context.ServiceProvider.GetService(typeof(IDiscordBotService)) as IDiscordBotService;
            if (discordBotService == null)
            {
                context.Logger.LogWarning("BotId={BotId}: Discord bot service is not available, skipping reaction sync", context.Bot.BotId);
                return;
            }

            var emojis = teamsData.Teams
                .Select(team => string.IsNullOrWhiteSpace(team.Emoji) ? "🎯" : team.Emoji)
                .ToList();

            var synced = await discordBotService.SyncBoardReactionsIfPresentAsync(context.Bot.BotId, emojis);
            if (!synced)
            {
                context.Logger.LogInformation("BotId={BotId}: Reaction sync skipped or failed after team update", context.Bot.BotId);
            }
        }

        /// <summary>
        /// Gets a string slash-command option value by name.
        /// </summary>
        private static string? GetStringOption(SocketSlashCommand command, string optionName)
        {
            return command.Data.Options.SelectMany(x => x.Options).FirstOrDefault(o => o.Name == optionName)?.Value as string;
        }

        /// <summary>
        /// Handles the Refresh Board admin button click: re-publishes the board message ephemerally.
        /// Only users with Administrator permission or the guild owner may use this.
        /// </summary>
        public async Task HandleRefreshBoardButtonAsync(
            SocketMessageComponent component,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context)
        {
            if (!user.GuildPermissions.Administrator && user.Id != guild.OwnerId)
            {
                await component.FollowupAsync("❌ Only administrators can refresh the board.", ephemeral: true);
                return;
            }

            try
            {
                var teamsData = await context.TeamsDataService.GetAsync(context.Bot.BotId);
                var botConfig = await context.BotDataService.GetAsync(context.Bot.BotId);
                var teamsBoard = BoardMessageFactory.FromTeams(teamsData, botConfig);

                var service = context.ServiceProvider.GetService(typeof(IDiscordBotService)) as IDiscordBotService;
                if (service == null)
                {
                    await component.FollowupAsync("Discord bot service is not available.", ephemeral: true);
                    return;
                }

                var refreshed = await service.RefreshBoardMessageAsync(context.Bot.BotId, teamsBoard);
                await component.FollowupAsync(
                    refreshed ? "✅ Board refreshed successfully." : "❌ Failed to refresh the board.",
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "BotId={BotId}: Error refreshing board via button", context.Bot.BotId);
                await component.FollowupAsync($"An error occurred: {ex.Message}", ephemeral: true);
            }
        }

        /// <summary>
        /// Handles the Add Team modal submission: validates inputs, creates the team and Discord role,
        /// saves to storage and refreshes the board message.
        /// Only users with Administrator permission or the guild owner may submit this form.
        /// </summary>
        public async Task HandleAddTeamModalAsync(
            SocketModal modal,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context)
        {
            if (!user.GuildPermissions.Administrator && user.Id != guild.OwnerId)
            {
                await modal.FollowupAsync("❌ Only administrators can add teams.", ephemeral: true);
                return;
            }

            var components = modal.Data.Components.ToList();
            var teamName = components.FirstOrDefault(c => c.CustomId == "team_name")?.Value?.Trim() ?? "";
            var leaderName = components.FirstOrDefault(c => c.CustomId == "leader_name")?.Value?.Trim() ?? "";
            var contact = components.FirstOrDefault(c => c.CustomId == "contact_info")?.Value?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(teamName) || string.IsNullOrWhiteSpace(leaderName))
            {
                await modal.FollowupAsync("Team name and leader name are required.", ephemeral: true);
                return;
            }

            try
            {
                var teamsData = await context.TeamsDataService.GetAsync(context.Bot.BotId);

                if (teamsData.Teams.Any(t => t.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase)))
                {
                    await modal.FollowupAsync($"❌ A team named **{teamName}** already exists.", ephemeral: true);
                    return;
                }

                var usedEmojis = teamsData.Teams
                    .Select(t => string.IsNullOrWhiteSpace(t.Emoji) ? "🎯" : t.Emoji.Trim())
                    .ToHashSet(StringComparer.Ordinal);

                var catalog = await _emojiCatalogService.GetEmojiCatalogAsync();
                var availableEmojis = catalog
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Select(e => e.Trim())
                    .Where(e => !usedEmojis.Contains(e))
                    .ToList();

                var emoji = availableEmojis.Count > 0
                    ? availableEmojis[Random.Shared.Next(availableEmojis.Count)]
                    : "🎯";

                var randomColor = new Color((uint)Random.Shared.Next(0x1000000));
                await guild.CreateRoleAsync(teamName, color: randomColor);

                var newTeam = new TeamDto
                {
                    Name = teamName,
                    LeaderName = leaderName,
                    Contact = contact,
                    Emoji = emoji
                };

                teamsData.Teams.Add(newTeam);
                await context.TeamsDataService.SaveAsync(context.Bot.BotId, teamsData);
                await RefreshBoardMessageAsync(context, teamsData);
                await SyncBoardReactionsIfPresentAsync(context, teamsData);

                context.Logger.LogInformation(
                    "BotId={BotId}: Team '{TeamName}' created via Add Team modal by user {UserId}",
                    context.Bot.BotId, teamName, user.Id);

                await modal.FollowupAsync($"✅ Team **{teamName}** was added successfully!", ephemeral: true);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "BotId={BotId}: Error adding team via modal", context.Bot.BotId);
                await modal.FollowupAsync($"An error occurred: {ex.Message}", ephemeral: true);
            }
        }
    }
}
