using Discord;
using Discord.WebSocket;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;

namespace BotManager.Backend.Shared.Models
{
    /// <summary>
    /// Interface for Discord bot plugins that handle commands and events for a specific bot instance.
    /// Each bot can have its own plugin with custom commands and behaviors.
    /// </summary>
    public interface IDiscordBotPlugin
    {
        /// <summary>
        /// Unique identifier for this plugin (e.g., "discord-alliance", "gaming-bot", etc.)
        /// </summary>
        string PluginId { get; }

        /// <summary>
        /// Friendly name for this plugin
        /// </summary>
        string PluginName { get; }

        /// <summary>
        /// Initialize the plugin when bot is starting. 
        /// Load configurations, register commands, set up event handlers.
        /// </summary>
        Task InitializeAsync(IPluginContext context);

        /// <summary>
        /// Shutdown the plugin when bot is stopping.
        /// Clean up resources, save state, unregister event handlers.
        /// </summary>
        Task ShutdownAsync(IPluginContext context);

        /// <summary>
        /// Register slash commands for this plugin on the Discord guild.
        /// Called after bot is ready and connected to server.
        /// </summary>
        Task RegisterCommandsAsync(SocketGuild guild, IPluginContext context);

        /// <summary>
        /// Handle a slash command execution for this plugin.
        /// Return true if command was handled, false otherwise.
        /// </summary>
        Task<bool> HandleCommandAsync(SocketSlashCommand command, IPluginContext context);

        /// <summary>
        /// Get all commands registered by this plugin (for admin listing).
        /// </summary>
        Task<IEnumerable<BotCommand>> GetRegisteredCommandsAsync(IPluginContext context);

        /// <summary>
        /// Handle a reaction being added to a message.
        /// </summary>
        Task HandleReactionAddedAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            ISocketMessageChannel channel,
            SocketReaction reaction,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context);

        /// <summary>
        /// Handle a reaction being removed from a message.
        /// </summary>
        Task HandleReactionRemovedAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            ISocketMessageChannel channel,
            SocketReaction reaction,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context);
    }
}
