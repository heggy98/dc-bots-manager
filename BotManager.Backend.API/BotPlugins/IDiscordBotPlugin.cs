using Discord.WebSocket;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;

namespace BotManager.Backend.API.BotPlugins
{
    /// <summary>
    /// Interface for Discord bot plugins that handle commands and events for a specific bot instance.
    /// Each bot can have its own plugin with custom commands and behaviors.
    /// </summary>
    public interface IDiscordBotPlugin
    {
        /// <summary>
        /// Unique identifier for this plugin (e.g., "discord-aliance", "gaming-bot", etc.)
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
        Task InitializeAsync(PluginContext context);

        /// <summary>
        /// Shutdown the plugin when bot is stopping.
        /// Clean up resources, save state, unregister event handlers.
        /// </summary>
        Task ShutdownAsync(PluginContext context);

        /// <summary>
        /// Register slash commands for this plugin on the Discord guild.
        /// Called after bot is ready and connected to server.
        /// </summary>
        Task RegisterCommandsAsync(SocketGuild guild, PluginContext context);

        /// <summary>
        /// Handle a slash command execution for this plugin.
        /// Return true if command was handled, false otherwise.
        /// </summary>
        Task<bool> HandleCommandAsync(SocketSlashCommand command, PluginContext context);

        /// <summary>
        /// Get all commands registered by this plugin (for admin listing).
        /// </summary>
        Task<IEnumerable<BotCommand>> GetRegisteredCommandsAsync(PluginContext context);
    }
}
