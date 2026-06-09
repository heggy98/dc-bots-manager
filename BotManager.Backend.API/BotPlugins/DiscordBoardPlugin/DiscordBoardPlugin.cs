using Discord;
using Discord.WebSocket;
using BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Services;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.BotPlugins.DiscordBoardPlugin
{
    /// <summary>
    /// Discord Board plugin - manages teams, roles, and team-based role assignment system.
    /// Commands are registered and dispatched by an injectable command service.
    /// </summary>
    public class DiscordBoardPlugin : IDiscordBotPlugin
    {
        public string PluginId => "discord-board";
        public string PluginName => "Discord Board Plugin";

        private bool _commandsRegistered = false;

        /// <summary>
        /// Initializes plugin runtime state for a bot.
        /// </summary>
        public async Task InitializeAsync(IPluginContext context)
        {
            context.Logger.LogInformation("Initializing Discord Board Plugin...");
            await Task.CompletedTask;
        }

        /// <summary>
        /// Shuts down plugin runtime state for a bot.
        /// </summary>
        public async Task ShutdownAsync(IPluginContext context)
        {
            context.Logger.LogInformation("Shutting down Discord Board Plugin...");
            _commandsRegistered = false;
            await Task.CompletedTask;
        }

        /// <summary>
        /// Registers slash commands for the plugin in a guild.
        /// </summary>
        public async Task RegisterCommandsAsync(SocketGuild guild, IPluginContext context)
        {
            if (_commandsRegistered)
            {
                context.Logger.LogInformation("Commands already registered for Discord Board Plugin");
                return;
            }

            var commandService = ResolveCommandService(context);
            if (commandService == null)
            {
                context.Logger.LogError("Command service is not available; commands were not registered");
                return;
            }

            var commands = commandService.BuildCommands();

            try
            {
                await guild.BulkOverwriteApplicationCommandAsync(commands.ToArray());
                _commandsRegistered = true;
                context.Logger.LogInformation("Registered {CommandCount} commands for Discord Board Plugin", commands.Count);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Failed to register commands for Discord Board Plugin");
            }
        }

        /// <summary>
        /// Dispatches a slash command to the board command service.
        /// </summary>
        public async Task<bool> HandleCommandAsync(SocketSlashCommand command, IPluginContext context)
        {
            var channel = command.Channel as SocketTextChannel;
            var user = command.User as SocketGuildUser;
            var guild = user?.Guild;

            if (channel == null || guild == null || user == null)
            {
                await command.FollowupAsync("This command can only be used in a guild text channel.", ephemeral: true);
                return false;
            }

            var commandService = ResolveCommandService(context);
            if (commandService == null)
            {
                await command.FollowupAsync("Command service is not available.", ephemeral: true);
                return false;
            }

            try
            {
                var handled = await commandService.DispatchAsync(command, guild, channel, user, context);
                if (!handled)
                {
                    await command.FollowupAsync("Unknown command.", ephemeral: true);
                }

                return handled;
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Error handling command {CommandName}", command.CommandName);
                await command.FollowupAsync($"Error while processing command: {ex.Message}", ephemeral: true);
                return false;
            }
        }

        /// <summary>
        /// Handles a reaction-added event for board message role assignment.
        /// </summary>
        public async Task HandleReactionAddedAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            ISocketMessageChannel channel,
            SocketReaction reaction,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context)
        {
            var commandService = ResolveCommandService(context);
            if (commandService == null)
            {
                context.Logger.LogWarning("Command service is not available for reaction add handling.");
                return;
            }

            await commandService.HandleReactionAddedAsync(cachedMessage, channel, reaction, guild, user, context);
        }

        /// <summary>
        /// Handles a reaction-removed event for board message role assignment.
        /// </summary>
        public async Task HandleReactionRemovedAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            ISocketMessageChannel channel,
            SocketReaction reaction,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context)
        {
            var commandService = ResolveCommandService(context);
            if (commandService == null)
            {
                context.Logger.LogWarning("Command service is not available for reaction remove handling.");
                return;
            }

            await commandService.HandleReactionRemovedAsync(cachedMessage, channel, reaction, guild, user, context);
        }

        /// <summary>
        /// Handles a team-toggle button interaction for board message role assignment.
        /// </summary>
        public async Task HandleButtonInteractionAsync(
            SocketMessageComponent component,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context)
        {
            var commandService = ResolveCommandService(context);
            if (commandService == null)
            {
                context.Logger.LogWarning("Command service is not available for button interaction handling.");
                await component.FollowupAsync("Command service is not available.", ephemeral: true);
                return;
            }

            await commandService.HandleButtonInteractionAsync(component, guild, user, context);
        }

        /// <summary>
        /// Handles an admin board action button click (e.g. Refresh Board).
        /// </summary>
        public async Task HandleBoardActionButtonAsync(
            SocketMessageComponent component,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context)
        {
            var commandService = ResolveCommandService(context);
            if (commandService == null)
            {
                context.Logger.LogWarning("Command service is not available for board action button handling.");
                await component.FollowupAsync("Command service is not available.", ephemeral: true);
                return;
            }

            await commandService.HandleBoardActionButtonAsync(component, guild, user, context);
        }

        /// <summary>
        /// Handles a board modal submission (e.g. the Add Team form).
        /// </summary>
        public async Task HandleBoardModalAsync(
            SocketModal modal,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context)
        {
            var commandService = ResolveCommandService(context);
            if (commandService == null)
            {
                context.Logger.LogWarning("Command service is not available for board modal handling.");
                await modal.FollowupAsync("Command service is not available.", ephemeral: true);
                return;
            }

            await commandService.HandleBoardModalAsync(modal, guild, user, context);
        }

        /// <summary>
        /// Returns command definitions stored for the current bot.
        /// </summary>
        public async Task<IEnumerable<BotCommand>> GetRegisteredCommandsAsync(IPluginContext context)
        {
            var commands = await context.DbContext.BotCommands
                .Where(c => c.BotId == context.Bot.BotId)
                .ToListAsync();

            return commands;
        }

        /// <summary>
        /// Resolves the board command service from the plugin service scope.
        /// </summary>
        private static IBoardCommandService? ResolveCommandService(IPluginContext context)
        {
            return context.ServiceProvider.GetService(typeof(IBoardCommandService)) as IBoardCommandService;
        }
    }
}
