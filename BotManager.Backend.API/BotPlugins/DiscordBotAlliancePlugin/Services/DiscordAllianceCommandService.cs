using Discord;
using Discord.WebSocket;
using BotManager.Backend.API.BotPlugins.DiscordBotAlliancePlugin.Handlers;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Models;

namespace BotManager.Backend.API.BotPlugins.DiscordBotAlliancePlugin.Services
{
    public class DiscordAllianceCommandService : IDiscordAllianceCommandService, IDiscordCommandProvider
    {
        private readonly TeamsCommandHandler _teamsHandler;
        private readonly ReactionsCommandHandler _reactionsHandler;
        private readonly ILogger<DiscordAllianceCommandService> _logger;
        private readonly IReadOnlyDictionary<string, CommandCatalogItem> _catalog;

        public DiscordAllianceCommandService(
            TeamsCommandHandler teamsHandler,
            ReactionsCommandHandler reactionsHandler,
            ILogger<DiscordAllianceCommandService> logger)
        {
            _teamsHandler = teamsHandler;
            _reactionsHandler = reactionsHandler;
            _logger = logger;
            _catalog = BuildCatalog();
        }

        public IReadOnlyCollection<ApplicationCommandProperties> BuildCommands()
        {
            // Return only the "board" parent command with all subcommands
            if (_catalog.TryGetValue("board", out var boardCommand) && boardCommand.BuildCommand != null)
            {
                return new List<ApplicationCommandProperties> { boardCommand.BuildCommand() };
            }
            return new List<ApplicationCommandProperties>();
        }

        public IReadOnlyCollection<DiscordCommandRegistration> GetCommandRegistrations()
        {
            // Return all subcommand registrations (excluding the parent "board" command)
            return _catalog.Values
                .Where(c => c.Registration.Name != "board")
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
                // Extract subcommand name from options
                var firstOption = command.Data.Options?.FirstOrDefault();
                var subcommandName = firstOption?.Name;
                if (string.IsNullOrEmpty(subcommandName))
                {
                    return false;
                }

                if (!_catalog.TryGetValue(subcommandName, out var commandItem))
                {
                    return false;
                }

                if (commandItem.RequiresAdmin && !user.GuildPermissions.Administrator)
                {
                    await command.FollowupAsync("You need Administrator permission to run this command.", ephemeral: true);
                    return false;
                }

                if (commandItem.Execute != null)
                {
                    await commandItem.Execute(command, guild, channel, context);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching command {CommandName}", command.CommandName);
                throw;
            }
        }

        public Task HandleReactionAddedAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            ISocketMessageChannel channel,
            SocketReaction reaction,
            SocketGuild guild,
            SocketGuildUser user,
            PluginContext context)
        {
            return _reactionsHandler.HandleReactionAddedAsync(cachedMessage, channel, reaction, guild, user, context);
        }

        public Task HandleReactionRemovedAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            ISocketMessageChannel channel,
            SocketReaction reaction,
            SocketGuild guild,
            SocketGuildUser user,
            PluginContext context)
        {
            return _reactionsHandler.HandleReactionRemovedAsync(cachedMessage, channel, reaction, guild, user, context);
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
                    BuildCommand: null,
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
                    BuildCommand: null,
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
                    BuildCommand: null,
                    Execute: (command, guild, channel, context) => _teamsHandler.HandleEditTeamAsync(command, guild, channel, context)),

                new CommandCatalogItem(
                    new DiscordCommandRegistration
                    {
                        Name = "publish-board",
                        Description = "Publishes plain or team board.",
                        MinPermissionLevel = 1,
                        UserHint = "Use /board publish-board type: teams|plain.",
                        SuccessMessage = "Board was published.",
                        PermissionMessage = "This command is admin-only.",
                        ErrorMessage = "Error: {0}"
                    },
                    RequiresAdmin: true,
                    BuildCommand: null,
                    Execute: (command, guild, channel, context) => _teamsHandler.HandlePublishBoardAsync(command, guild, channel, context)),

                new CommandCatalogItem(
                    new DiscordCommandRegistration
                    {
                        Name = "refresh",
                        Description = "Refreshes the current team board.",
                        MinPermissionLevel = 1,
                        UserHint = "Use /board refresh.",
                        SuccessMessage = "Team board was refreshed.",
                        PermissionMessage = "This command is admin-only.",
                        ErrorMessage = "Error: {0}"
                    },
                    RequiresAdmin: true,
                    BuildCommand: null,
                    Execute: (command, guild, channel, context) => _teamsHandler.HandleShowTeamsListAsync(command, guild, channel, context)),

                new CommandCatalogItem(
                    new DiscordCommandRegistration
                    {
                        Name = "insert",
                        Description = "Inserts the board into the configured channel.",
                        MinPermissionLevel = 1,
                        UserHint = "Use /board insert.",
                        SuccessMessage = "Board was inserted.",
                        PermissionMessage = "This command is admin-only.",
                        ErrorMessage = "Error: {0}"
                    },
                    RequiresAdmin: true,
                    BuildCommand: null,
                    Execute: (command, guild, channel, context) => _teamsHandler.HandlePublishBoardAsync(command, guild, channel, context)),

                new CommandCatalogItem(
                    new DiscordCommandRegistration
                    {
                        Name = "sync-reactions",
                        Description = "Regenerates emoji reactions on the board message.",
                        MinPermissionLevel = 1,
                        UserHint = "Use /board sync-reactions.",
                        SuccessMessage = "Board reactions were synchronized.",
                        PermissionMessage = "This command is admin-only.",
                        ErrorMessage = "Error: {0}"
                    },
                    RequiresAdmin: true,
                    BuildCommand: null,
                    Execute: (command, guild, channel, context) => _reactionsHandler.HandleSyncBoardReactionsAsync(command, guild, channel, context)),

                new CommandCatalogItem(
                    new DiscordCommandRegistration
                    {
                        Name = "board",
                        Description = "Board management commands.",
                        MinPermissionLevel = 0,
                        UserHint = "Use /board with subcommands.",
                        SuccessMessage = "Command executed.",
                        PermissionMessage = "You do not have permission to run this command.",
                        ErrorMessage = "Error: {0}"
                    },
                    RequiresAdmin: false,
                    BuildCommand: BuildBoardCommand,
                    Execute: null)
            };

            return entries.ToDictionary(c => c.Registration.Name, StringComparer.OrdinalIgnoreCase);
        }

        private ApplicationCommandProperties BuildBoardCommand()
        {
            var boardCommand = new SlashCommandBuilder()
                .WithName("board")
                .WithDescription("Board management commands");

            // Add subcommands
            boardCommand.AddOption(new SlashCommandOptionBuilder()
                .WithName("add-team")
                .WithDescription("Adds a new team to the roster.")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("name", ApplicationCommandOptionType.String, "Team name.", isRequired: true)
                .AddOption("leader", ApplicationCommandOptionType.String, "Team leader name.", isRequired: true)
                .AddOption("contact", ApplicationCommandOptionType.String, "Team contact information.", isRequired: true));

            boardCommand.AddOption(new SlashCommandOptionBuilder()
                .WithName("remove-team")
                .WithDescription("Removes a team from the roster.")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("name", ApplicationCommandOptionType.String, "Team name to remove.", isRequired: true));

            boardCommand.AddOption(new SlashCommandOptionBuilder()
                .WithName("edit-team")
                .WithDescription("Edits an existing team.")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("name", ApplicationCommandOptionType.String, "Current team name.", isRequired: true)
                .AddOption("new-name", ApplicationCommandOptionType.String, "New team name.", isRequired: false)
                .AddOption("new-leader", ApplicationCommandOptionType.String, "New team leader.", isRequired: false)
                .AddOption("new-contact", ApplicationCommandOptionType.String, "New contact info.", isRequired: false));

            boardCommand.AddOption(new SlashCommandOptionBuilder()
                .WithName("publish-board")
                .WithDescription("Publishes a board as plain or team-based (Admin)")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("type")
                    .WithDescription("Board type")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true)
                    .AddChoice("Team board", "teams")
                    .AddChoice("Plain board", "plain"))
                .AddOption("title", ApplicationCommandOptionType.String, "Board title override.", isRequired: false)
                .AddOption("description", ApplicationCommandOptionType.String, "Board description for plain board.", isRequired: false));

            boardCommand.AddOption(new SlashCommandOptionBuilder()
                .WithName("refresh")
                .WithDescription("Shows and refreshes the team board (Admin)")
                .WithType(ApplicationCommandOptionType.SubCommand));

            boardCommand.AddOption(new SlashCommandOptionBuilder()
                .WithName("insert")
                .WithDescription("Inserts the board into the configured channel (Admin)")
                .WithType(ApplicationCommandOptionType.SubCommand));

            boardCommand.AddOption(new SlashCommandOptionBuilder()
                .WithName("sync-reactions")
                .WithDescription("Regenerates reaction emojis on the board message (Admin)")
                .WithType(ApplicationCommandOptionType.SubCommand));

            return boardCommand.Build();
        }

        private sealed record CommandCatalogItem(
            DiscordCommandRegistration Registration,
            bool RequiresAdmin,
            Func<ApplicationCommandProperties>? BuildCommand,
            Func<SocketSlashCommand, SocketGuild, SocketTextChannel, PluginContext, Task>? Execute);
    }
}
