using Discord;
using Discord.WebSocket;
using BotManager.Api.BotPlugins.DiscordBotAlliancePlugin.Handlers;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Models;

namespace BotManager.Api.BotPlugins.DiscordBotAlliancePlugin.Services
{
    public class DiscordAllianceCommandService : IDiscordAllianceCommandService, IDiscordCommandProvider
    {
        private readonly TeamsCommandHandler _teamsHandler;
        private readonly EmojisCommandHandler _emojisHandler;
        private readonly ReactionsCommandHandler _reactionsHandler;
        private readonly ILogger<DiscordAllianceCommandService> _logger;
        private readonly IReadOnlyDictionary<string, CommandCatalogItem> _catalog;

        public DiscordAllianceCommandService(
            TeamsCommandHandler teamsHandler,
            EmojisCommandHandler emojisHandler,
            ReactionsCommandHandler reactionsHandler,
            ILogger<DiscordAllianceCommandService> logger)
        {
            _teamsHandler = teamsHandler;
            _emojisHandler = emojisHandler;
            _reactionsHandler = reactionsHandler;
            _logger = logger;
            _catalog = BuildCatalog();
        }

        public IReadOnlyCollection<ApplicationCommandProperties> BuildCommands()
        {
            return _catalog.Values.Select(c => c.BuildCommand()).ToList();
        }

        public IReadOnlyCollection<DiscordCommandRegistration> GetCommandRegistrations()
        {
            return _catalog.Values
                .Select(c => c.Registration)
                .ToList();
        }

        public async Task<bool> DispatchAsync(
            SocketSlashCommand command,
            SocketGuild guild,
            SocketTextChannel channel,
            SocketGuildUser user,
            PluginContext context)
        {
            try
            {
                if (!_catalog.TryGetValue(command.CommandName, out var commandItem))
                {
                    return false;
                }

                if (commandItem.RequiresAdmin && !user.GuildPermissions.Administrator)
                {
                    await command.FollowupAsync("You need Administrator permission to run this command.", ephemeral: true);
                    return false;
                }

                await commandItem.Execute(command, guild, channel, context);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching command {CommandName}", command.CommandName);
                throw;
            }
        }

        private IReadOnlyDictionary<string, CommandCatalogItem> BuildCatalog()
        {
            var entries = new[]
            {
                new CommandCatalogItem(
                    new DiscordCommandRegistration
                    {
                        Name = "add-team",
                        Description = "Adds a new team to the roster.",
                        MinPermissionLevel = 0,
                        UserHint = "Provide team name, leader, and contact.",
                        SuccessMessage = "Team '{0}' was added successfully.",
                        PermissionMessage = "You do not have permission to run this command.",
                        ErrorMessage = "Error: {0}"
                    },
                    RequiresAdmin: false,
                    BuildCommand: () => new SlashCommandBuilder()
                        .WithName("add-team")
                        .WithDescription("Adds a new team to the roster.")
                        .AddOption("name", ApplicationCommandOptionType.String, "Team name.", isRequired: true)
                        .AddOption("leader", ApplicationCommandOptionType.String, "Team leader name.", isRequired: true)
                        .AddOption("contact", ApplicationCommandOptionType.String, "Team contact information.", isRequired: true)
                        .Build(),
                    Execute: (command, guild, channel, context) => _teamsHandler.HandleAddTeamAsync(command, guild, channel, context)),

                new CommandCatalogItem(
                    new DiscordCommandRegistration
                    {
                        Name = "remove-team",
                        Description = "Removes a team from the roster (including role).",
                        MinPermissionLevel = 0,
                        UserHint = "Provide the team name to remove.",
                        SuccessMessage = "Team '{0}' was removed successfully.",
                        PermissionMessage = "You do not have permission to run this command.",
                        ErrorMessage = "Error: {0}"
                    },
                    RequiresAdmin: false,
                    BuildCommand: () => new SlashCommandBuilder()
                        .WithName("remove-team")
                        .WithDescription("Removes a team from the roster.")
                        .AddOption("name", ApplicationCommandOptionType.String, "Team name to remove.", isRequired: true)
                        .Build(),
                    Execute: (command, guild, channel, context) => _teamsHandler.HandleRemoveTeamAsync(command, guild, channel, context)),

                new CommandCatalogItem(
                    new DiscordCommandRegistration
                    {
                        Name = "edit-team",
                        Description = "Edits team data.",
                        MinPermissionLevel = 0,
                        UserHint = "Provide team name and updated values.",
                        SuccessMessage = "Team '{0}' was updated.",
                        PermissionMessage = "You do not have permission to run this command.",
                        ErrorMessage = "Error: {0}"
                    },
                    RequiresAdmin: false,
                    BuildCommand: () => new SlashCommandBuilder()
                        .WithName("edit-team")
                        .WithDescription("Edits an existing team.")
                        .AddOption("name", ApplicationCommandOptionType.String, "Current team name.", isRequired: true)
                        .AddOption("new-name", ApplicationCommandOptionType.String, "New team name.", isRequired: false)
                        .AddOption("new-leader", ApplicationCommandOptionType.String, "New team leader.", isRequired: false)
                        .AddOption("new-contact", ApplicationCommandOptionType.String, "New contact info.", isRequired: false)
                        .Build(),
                    Execute: (command, guild, channel, context) => _teamsHandler.HandleEditTeamAsync(command, guild, channel, context)),

                new CommandCatalogItem(
                    new DiscordCommandRegistration
                    {
                        Name = "publish-board",
                        Description = "Publishes plain or team board.",
                        MinPermissionLevel = 1,
                        UserHint = "Use /publish-board type: teams|plain.",
                        SuccessMessage = "Board was published.",
                        PermissionMessage = "This command is admin-only.",
                        ErrorMessage = "Error: {0}"
                    },
                    RequiresAdmin: true,
                    BuildCommand: () => new SlashCommandBuilder()
                        .WithName("publish-board")
                        .WithDescription("Publishes a board as plain or team-based (Admin)")
                        .AddOption(new SlashCommandOptionBuilder()
                            .WithName("type")
                            .WithDescription("Board type")
                            .WithType(ApplicationCommandOptionType.String)
                            .WithRequired(true)
                            .AddChoice("Team board", "teams")
                            .AddChoice("Plain board", "plain"))
                        .AddOption("title", ApplicationCommandOptionType.String, "Board title override.", isRequired: false)
                        .AddOption("description", ApplicationCommandOptionType.String, "Board description for plain board.", isRequired: false)
                        .Build(),
                    Execute: (command, guild, channel, context) => _teamsHandler.HandlePublishBoardAsync(command, guild, channel, context)),

                new CommandCatalogItem(
                    new DiscordCommandRegistration
                    {
                        Name = "list-teams",
                        Description = "Publishes the current team board.",
                        MinPermissionLevel = 1,
                        UserHint = "Use /list-teams.",
                        SuccessMessage = "Team board was refreshed.",
                        PermissionMessage = "This command is admin-only.",
                        ErrorMessage = "Error: {0}"
                    },
                    RequiresAdmin: true,
                    BuildCommand: () => new SlashCommandBuilder()
                        .WithName("list-teams")
                        .WithDescription("Shows and refreshes the team board (Admin)")
                        .Build(),
                    Execute: (command, guild, channel, context) => _teamsHandler.HandleShowTeamsListAsync(command, guild, channel, context)),

                new CommandCatalogItem(
                    new DiscordCommandRegistration
                    {
                        Name = "reorganize-emojis",
                        Description = "Reorganizes team emojis.",
                        MinPermissionLevel = 1,
                        UserHint = "Use /reorganize-emojis.",
                        SuccessMessage = "Team emojis were reorganized.",
                        PermissionMessage = "This command is admin-only.",
                        ErrorMessage = "Error: {0}"
                    },
                    RequiresAdmin: true,
                    BuildCommand: () => new SlashCommandBuilder()
                        .WithName("reorganize-emojis")
                        .WithDescription("Reorganizes team emojis and refreshes board (Admin)")
                        .Build(),
                    Execute: (command, guild, channel, context) => _emojisHandler.HandleReorganizeEmojisAsync(command, guild, channel, context)),

                new CommandCatalogItem(
                    new DiscordCommandRegistration
                    {
                        Name = "move-reactions",
                        Description = "Moves the reaction-role message to another channel.",
                        MinPermissionLevel = 1,
                        UserHint = "Choose a target text channel.",
                        SuccessMessage = "Reaction message was moved successfully.",
                        PermissionMessage = "This command is admin-only.",
                        ErrorMessage = "Error: {0}"
                    },
                    RequiresAdmin: true,
                    BuildCommand: () => new SlashCommandBuilder()
                        .WithName("move-reactions")
                        .WithDescription("Moves role reaction message to another channel (Admin)")
                        .AddOption("target-channel", ApplicationCommandOptionType.Channel, "Target text channel.", isRequired: true)
                        .Build(),
                    Execute: (command, guild, channel, context) => _reactionsHandler.HandleMoveReactionMessageAsync(command, guild, context))
            };

            return entries.ToDictionary(c => c.Registration.Name, StringComparer.OrdinalIgnoreCase);
        }

        private sealed record CommandCatalogItem(
            DiscordCommandRegistration Registration,
            bool RequiresAdmin,
            Func<ApplicationCommandProperties> BuildCommand,
            Func<SocketSlashCommand, SocketGuild, SocketTextChannel, PluginContext, Task> Execute);
    }
}
