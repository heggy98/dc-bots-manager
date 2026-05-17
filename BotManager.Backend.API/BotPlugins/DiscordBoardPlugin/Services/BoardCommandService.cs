using Discord;
using Discord.WebSocket;
using BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Handlers;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Models;
using BotManager.Backend.Shared.Models;

namespace BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Services
{
    /// <summary>
    /// Provides slash command schema and dispatching for board plugin commands.
    /// </summary>
    public class BoardCommandService : IBoardCommandService, IDiscordCommandProvider
    {
        private readonly TeamsCommandHandler _teamsHandler;
        private readonly ReactionsCommandHandler _reactionsHandler;
        private readonly ILogger<BoardCommandService> _logger;

        private sealed record CommandListItem(
        DiscordCommandRegistration Registration,
        bool RequiresAdmin,
        Func<ApplicationCommandProperties>? BuildCommand,
        Func<SocketSlashCommand, SocketGuild, SocketTextChannel, IPluginContext, Task>? Execute);
    
        private readonly IReadOnlyDictionary<string, CommandListItem> _commandList;

        /// <summary>
        /// Creates a new board command service.
        /// </summary>
        public BoardCommandService(
            TeamsCommandHandler teamsHandler,
            ReactionsCommandHandler reactionsHandler,
            ILogger<BoardCommandService> logger)
        {
            _teamsHandler = teamsHandler;
            _reactionsHandler = reactionsHandler;
            _logger = logger;
            _commandList = BuildCommandList();
        }

        /// <summary>
        /// Builds application command definitions to register in Discord.
        /// </summary>
        public IReadOnlyCollection<ApplicationCommandProperties> BuildCommands()
        {
            // Return only the "board" parent command with all subcommands
            if (_commandList.TryGetValue("board", out var boardCommand) && boardCommand.BuildCommand != null)
            {
                return new List<ApplicationCommandProperties> { boardCommand.BuildCommand() };
            }
            return new List<ApplicationCommandProperties>();
        }

        /// <summary>
        /// Returns command registrations persisted by bot lifecycle services.
        /// </summary>
        public IReadOnlyCollection<DiscordCommandRegistration> GetCommandRegistrations()
        {
            // Return all subcommand registrations (excluding the parent "board" command)
            return _commandList.Values
                .Where(c => c.Registration.Name != "board")
                .Select(c => c.Registration)
                .ToList();
        }

            /// <summary>
            /// Dispatches a slash command to the mapped board subcommand handler.
            /// </summary>
        public async Task<bool> DispatchAsync(
            SocketSlashCommand command,
            SocketGuild guild,
            SocketTextChannel channel,
            SocketGuildUser user,
            IPluginContext context)
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

                if (!_commandList.TryGetValue(subcommandName, out var commandItem))
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

        /// <summary>
        /// Routes reaction-added events to the reactions handler.
        /// </summary>
        public Task HandleReactionAddedAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            ISocketMessageChannel channel,
            SocketReaction reaction,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context)
        {
            return _reactionsHandler.HandleReactionAddedAsync(cachedMessage, channel, reaction, guild, user, context);
        }

        /// <summary>
        /// Routes reaction-removed events to the reactions handler.
        /// </summary>
        public Task HandleReactionRemovedAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            ISocketMessageChannel channel,
            SocketReaction reaction,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context)
        {
            return _reactionsHandler.HandleReactionRemovedAsync(cachedMessage, channel, reaction, guild, user, context);
        }

        /// <summary>
        /// Builds an in-memory command catalog used for dispatch and registration metadata.
        /// </summary>
        private IReadOnlyDictionary<string, CommandListItem> BuildCommandList()
        {
            var entries = new[]
            {
                new CommandListItem(
                    new DiscordCommandRegistration
                    {
                        Name = "board",
                        SubCommandName = "add-team",
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

                new CommandListItem(
                    new DiscordCommandRegistration
                    {
                        Name = "board",
                        SubCommandName = "remove-team",
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

                new CommandListItem(
                    new DiscordCommandRegistration
                    {
                        Name = "board",
                        SubCommandName = "edit-team",
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

                new CommandListItem(
                    new DiscordCommandRegistration
                    {
                        Name = "board",
                        SubCommandName = "refresh",
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

                new CommandListItem(
                    new DiscordCommandRegistration
                    {
                        Name = "board",
                        SubCommandName = "insert",
                        Description = "Inserts the board into the configured channel.",
                        MinPermissionLevel = 1,
                        UserHint = "Use /board insert.",
                        SuccessMessage = "Board was inserted.",
                        PermissionMessage = "This command is admin-only.",
                        ErrorMessage = "Error: {0}"
                    },
                    RequiresAdmin: true,
                    BuildCommand: null,
                    Execute: (command, guild, channel, context) => _teamsHandler.HandleInsertBoardAsync(command, guild, channel, context)),

                new CommandListItem(
                    new DiscordCommandRegistration
                    {
                        Name = "board",
                        SubCommandName = "sync-reactions",
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

                new CommandListItem(
                    new DiscordCommandRegistration
                    {
                        Name = "board",
                        SubCommandName = null,
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

            // Create catalog: key is subcommand name (or "board" for parent)
            var dict = new Dictionary<string, CommandListItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in entries)
            {
                var key = item.Registration.SubCommandName ?? item.Registration.Name;
                dict[key] = item;
            }
            return dict;
        }

        /// <summary>
        /// Builds the parent /board slash command including subcommands.
        /// </summary>
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
    }
}
