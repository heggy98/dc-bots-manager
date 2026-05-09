using Discord;
using Discord.WebSocket;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using BotManager.Api.BotPlugins.DiscordBotAlliancePlugin.Handlers;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BotManager.Api.BotPlugins.DiscordBotAlliancePlugin
{
    /// <summary>
    /// Discord Bot Alliance plugin - manages teams, roles, and team-based role assignment system.
    /// Commands: /pridat-tym, /odebrat-tym, /upravit-tym, /seznam-tymu, /reorganizeemojis, /presun-reakce
    /// </summary>
    public class DiscordBotAlliancePlugin : IDiscordBotPlugin
    {
        public string PluginId => "discord-aliance";
        public string PluginName => "Discord Bot Alliance";

        private readonly TeamsCommandHandler _teamsHandler = new();
        private readonly EmojisCommandHandler _emojisHandler = new();
        private readonly ReactionsCommandHandler _reactionsHandler = new();

        private SocketGuild? _cachedGuild;
        private readonly string _channelNameTabule = "seznam-týmů";
        private readonly ulong _adminUserId = 347490212097687552;
        private bool _commandsRegistered = false;

        public async Task InitializeAsync(PluginContext context)
        {
            context.Logger.LogInformation("Initializing Discord Bot Alliance Plugin...");
            // Plugin initialization - load configuration, set up caches, etc.
            await Task.CompletedTask;
        }

        public async Task ShutdownAsync(PluginContext context)
        {
            context.Logger.LogInformation("Shutting down Discord Bot Alliance Plugin...");
            _commandsRegistered = false;
            _cachedGuild = null;
            await Task.CompletedTask;
        }

        public async Task RegisterCommandsAsync(SocketGuild guild, PluginContext context)
        {
            if (_commandsRegistered)
            {
                context.Logger.LogInformation("Commands already registered for Discord Bot Alliance Plugin");
                return;
            }

            _cachedGuild = guild;

            var commands = new List<ApplicationCommandProperties>();

            // 1. /pridat-tym - Add team
            var addTeamCommand = new SlashCommandBuilder()
                .WithName("pridat-tym")
                .WithDescription("Přidá nový tým do seznamu.")
                .AddOption("nazev", ApplicationCommandOptionType.String, "Název týmu.", isRequired: true)
                .AddOption("velitel", ApplicationCommandOptionType.String, "Jméno velitele týmu.", isRequired: true)
                .AddOption("kontakt", ApplicationCommandOptionType.String, "Kontaktní informace na tým. Většinou je to mobil na velitele.", isRequired: true);
            commands.Add(addTeamCommand.Build());

            // 2. /odebrat-tym - Remove team
            var removeTeamCommand = new SlashCommandBuilder()
                .WithName("odebrat-tym")
                .WithDescription("Odebere tým ze seznamu (včetně role).")
                .AddOption("nazev", ApplicationCommandOptionType.String, "Název týmu k odebrání.", isRequired: true);
            commands.Add(removeTeamCommand.Build());

            // 3. /upravit-tym - Edit team
            var editTeamCommand = new SlashCommandBuilder()
                .WithName("upravit-tym")
                .WithDescription("Upraví data týmu")
                .AddOption("nazev", ApplicationCommandOptionType.String, "Název týmu k úpravě.", isRequired: true)
                .AddOption("novy-nazev", ApplicationCommandOptionType.String, "Nový název týmu (nepovinné)", isRequired: false)
                .AddOption("novy-velitel", ApplicationCommandOptionType.String, "Nový velitel (nepovinné)", isRequired: false)
                .AddOption("novy-kontakt", ApplicationCommandOptionType.String, "Nový kontakt (nepovinné)", isRequired: false);
            commands.Add(editTeamCommand.Build());

            // 4. /seznam-tymu - Show teams list (ADMIN)
            var teamsListCommand = new SlashCommandBuilder()
                .WithName("seznam-tymu")
                .WithDescription("Zobrazí seznam všech týmů (Admin)")
                .AddOption("aktualizovat", ApplicationCommandOptionType.Boolean, "Aktualizovat zprávu v kanálu (Default: false)", isRequired: false);
            commands.Add(teamsListCommand.Build());

            // 5. /reorganizeemojis - Reorganize emojis (ADMIN)
            var reorganizeEmojisCommand = new SlashCommandBuilder()
                .WithName("reorganizeemojis")
                .WithDescription("Reorganizuje emoji zprávu (Admin)")
                .AddOption("pokus", ApplicationCommandOptionType.Integer, "Číslo pokusu (Default: 1)", isRequired: false);
            commands.Add(reorganizeEmojisCommand.Build());

            // 6. /presun-reakce - Move reaction message (ADMIN)
            var moveReactionCommand = new SlashCommandBuilder()
                .WithName("presun-reakce")
                .WithDescription("Přesune zprávu pro výběr role do nového kanálu (Admin)");
            commands.Add(moveReactionCommand.Build());

            try
            {
                // Register commands to the guild (not globally)
                await guild.BulkOverwriteApplicationCommandAsync(commands.ToArray());
                _commandsRegistered = true;
                context.Logger.LogInformation("Registered {CommandCount} commands for Discord Bot Alliance Plugin", commands.Count);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Failed to register commands for Discord Bot Alliance Plugin");
            }
        }

        public async Task<bool> HandleCommandAsync(SocketSlashCommand command, PluginContext context)
        {
            // Defer response immediately
            await command.DeferAsync(ephemeral: true);

            var channel = command.Channel as SocketTextChannel;
            var user = command.User as SocketGuildUser;
            var guild = user?.Guild;

            if (channel == null || guild == null)
            {
                await command.FollowupAsync("Tento příkaz lze použít pouze na serveru.", ephemeral: true);
                return false;
            }

            try
            {
                switch (command.CommandName)
                {
                    case "pridat-tym":
                        await _teamsHandler.HandleAddTeamAsync(command, guild, channel, context);
                        return true;

                    case "odebrat-tym":
                        await _teamsHandler.HandleRemoveTeamAsync(command, guild, channel, context);
                        return true;

                    case "upravit-tym":
                        await _teamsHandler.HandleEditTeamAsync(command, guild, channel, context);
                        return true;

                    case "seznam-tymu":
                        if (!user.GuildPermissions.Administrator)
                        {
                            await command.FollowupAsync("Pro spuštění tohoto příkazu nemáš oprávnění Administrátora.", ephemeral: true);
                            return false;
                        }
                        await _teamsHandler.HandleShowTeamsListAsync(command, guild, channel, context);
                        return true;

                    case "reorganizeemojis":
                        if (!user.GuildPermissions.Administrator)
                        {
                            await command.FollowupAsync("Pro spuštění tohoto příkazu nemáš oprávnění Administrátora.", ephemeral: true);
                            return false;
                        }
                        await _emojisHandler.HandleReorganizeEmojisAsync(command, guild, channel, context);
                        return true;

                    case "presun-reakce":
                        if (!user.GuildPermissions.Administrator)
                        {
                            await command.FollowupAsync("Pro spuštění tohoto příkazu nemáš oprávnění Administrátora.", ephemeral: true);
                            return false;
                        }
                        await _reactionsHandler.HandleMoveReactionMessageAsync(command, guild, context);
                        return true;

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Error handling command {CommandName}", command.CommandName);
                await command.FollowupAsync($"Chyba při zpracování příkazu: {ex.Message}", ephemeral: true);
                return false;
            }
        }

        public async Task<IEnumerable<BotCommand>> GetRegisteredCommandsAsync(PluginContext context)
        {
            // Return all registered commands from database for this bot and plugin
            var commands = await context.DbContext.BotCommands
                .Where(c => c.BotId == context.Bot.BotId)
                .ToListAsync();

            return commands;
        }
    }
}
