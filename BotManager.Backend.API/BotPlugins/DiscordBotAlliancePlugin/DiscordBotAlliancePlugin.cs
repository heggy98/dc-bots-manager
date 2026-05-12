using Discord;
using Discord.WebSocket;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.API.BotPlugins.DiscordBotAlliancePlugin.Services;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.BotPlugins.DiscordBotAlliancePlugin
{
    /// <summary>
    /// Discord Bot Alliance plugin - manages teams, roles, and team-based role assignment system.
    /// Commands are registered and dispatched by an injectable command service.
    /// </summary>
    public class DiscordBotAlliancePlugin : IDiscordBotPlugin
    {
        public string PluginId => "discord-aliance";
        public string PluginName => "Discord Bot Alliance";

        private bool _commandsRegistered = false;

        public async Task InitializeAsync(PluginContext context)
        {
            context.Logger.LogInformation("Initializing Discord Bot Alliance Plugin...");
            await Task.CompletedTask;
        }

        public async Task ShutdownAsync(PluginContext context)
        {
            context.Logger.LogInformation("Shutting down Discord Bot Alliance Plugin...");
            _commandsRegistered = false;
            await Task.CompletedTask;
        }

        public async Task RegisterCommandsAsync(SocketGuild guild, PluginContext context)
        {
            if (_commandsRegistered)
            {
                context.Logger.LogInformation("Commands already registered for Discord Bot Alliance Plugin");
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
                context.Logger.LogInformation("Registered {CommandCount} commands for Discord Bot Alliance Plugin", commands.Count);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Failed to register commands for Discord Bot Alliance Plugin");
            }
        }

        public async Task<bool> HandleCommandAsync(SocketSlashCommand command, PluginContext context)
        {
            await command.DeferAsync(ephemeral: true);

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

        public async Task HandleReactionAddedAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            ISocketMessageChannel channel,
            SocketReaction reaction,
            SocketGuild guild,
            SocketGuildUser user,
            PluginContext context)
        {
            var commandService = ResolveCommandService(context);
            if (commandService == null)
            {
                context.Logger.LogWarning("Command service is not available for reaction add handling.");
                return;
            }

            await commandService.HandleReactionAddedAsync(cachedMessage, channel, reaction, guild, user, context);
        }

        public async Task HandleReactionRemovedAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            ISocketMessageChannel channel,
            SocketReaction reaction,
            SocketGuild guild,
            SocketGuildUser user,
            PluginContext context)
        {
            var commandService = ResolveCommandService(context);
            if (commandService == null)
            {
                context.Logger.LogWarning("Command service is not available for reaction remove handling.");
                return;
            }

            await commandService.HandleReactionRemovedAsync(cachedMessage, channel, reaction, guild, user, context);
        }

        public async Task<IEnumerable<BotCommand>> GetRegisteredCommandsAsync(PluginContext context)
        {
            var commands = await context.DbContext.BotCommands
                .Where(c => c.BotId == context.Bot.BotId)
                .ToListAsync();

            return commands;
        }

        private static IDiscordAllianceCommandService? ResolveCommandService(PluginContext context)
        {
            return context.ServiceProvider.GetService(typeof(IDiscordAllianceCommandService)) as IDiscordAllianceCommandService;
        }
    }
}
