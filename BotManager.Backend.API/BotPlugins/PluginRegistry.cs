using BotManager.Backend.Entities;
using Microsoft.Extensions.Logging;

namespace BotManager.Backend.API.BotPlugins
{
    /// <summary>
    /// Registry for managing bot plugins. Handles plugin discovery, loading, and execution.
    /// </summary>
    public class PluginRegistry
    {
        private readonly Dictionary<string, Type> _registeredPlugins = new();
        private readonly Dictionary<int, IDiscordBotPlugin> _activePlugins = new();
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PluginRegistry> _logger;

        public PluginRegistry(IServiceProvider serviceProvider, ILogger<PluginRegistry> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            RegisterDefaultPlugins();
        }

        /// <summary>
        /// Register default plugins that come with the system
        /// </summary>
        private void RegisterDefaultPlugins()
        {
            RegisterPlugin("discord-aliance", typeof(DiscordBotAlliancePlugin.DiscordBotAlliancePlugin));
        }

        /// <summary>
        /// Register a plugin type with the registry
        /// </summary>
        public void RegisterPlugin(string pluginId, Type pluginType)
        {
            if (!typeof(IDiscordBotPlugin).IsAssignableFrom(pluginType))
            {
                throw new ArgumentException($"Plugin type {pluginType.Name} must implement IDiscordBotPlugin", nameof(pluginType));
            }

            _registeredPlugins[pluginId] = pluginType;
            _logger.LogInformation("Registered plugin: {PluginId} ({PluginType})", pluginId, pluginType.Name);
        }

        /// <summary>
        /// Get or create a plugin instance for a bot
        /// </summary>
        public IDiscordBotPlugin GetOrCreatePlugin(int botId, string pluginId)
        {
            if (_activePlugins.TryGetValue(botId, out var plugin))
            {
                return plugin;
            }

            if (!_registeredPlugins.TryGetValue(pluginId, out var pluginType))
            {
                throw new KeyNotFoundException($"Plugin '{pluginId}' not found in registry");
            }

            var instance = Activator.CreateInstance(pluginType) as IDiscordBotPlugin;
            if (instance == null)
            {
                throw new InvalidOperationException($"Failed to instantiate plugin {pluginId}");
            }

            _activePlugins[botId] = instance;
            _logger.LogInformation("Created plugin instance for bot {BotId}: {PluginId}", botId, pluginId);
            return instance;
        }

        /// <summary>
        /// Remove a plugin instance
        /// </summary>
        public void RemovePlugin(int botId)
        {
            _activePlugins.Remove(botId);
            _logger.LogInformation("Removed plugin instance for bot {BotId}", botId);
        }

        /// <summary>
        /// Get the plugin for a specific bot
        /// </summary>
        public IDiscordBotPlugin? GetPlugin(int botId)
        {
            _activePlugins.TryGetValue(botId, out var plugin);
            return plugin;
        }

        /// <summary>
        /// Get all registered plugin IDs
        /// </summary>
        public IEnumerable<string> GetRegisteredPluginIds() => _registeredPlugins.Keys;

        /// <summary>
        /// Check if a plugin is registered
        /// </summary>
        public bool IsPluginRegistered(string pluginId) => _registeredPlugins.ContainsKey(pluginId);
    }
}
