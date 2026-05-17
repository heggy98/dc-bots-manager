using BotManager.Backend.Entities;

namespace BotManager.Backend.Shared.Models
{
    /// <summary>
    /// Registry for managing bot plugins. Handles plugin discovery, loading, and execution.
    /// </summary>
    public interface IPluginRegistry
    {
        /// <summary>
        /// Register a plugin type with the registry
        /// </summary>
        void RegisterPlugin(string pluginId, Type pluginType);

        /// <summary>
        /// Get or create a plugin instance for a bot
        /// </summary>
        IDiscordBotPlugin GetOrCreatePlugin(int botId, string pluginId);

        /// <summary>
        /// Remove a plugin instance
        /// </summary>
        void RemovePlugin(int botId);

        /// <summary>
        /// Get the plugin for a specific bot
        /// </summary>
        IDiscordBotPlugin? GetPlugin(int botId);

        /// <summary>
        /// Get all registered plugin IDs
        /// </summary>
        IEnumerable<string> GetRegisteredPluginIds();

        /// <summary>
        /// Check if a plugin is registered
        /// </summary>
        bool IsPluginRegistered(string pluginId);
    }
}
