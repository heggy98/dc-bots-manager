using BotManager.Backend.Shared.Models;
using Microsoft.Extensions.Logging;

namespace BotManager.Backend.API.BotPlugins
{
    /// <summary>
    /// Registry for managing bot plugins. Handles plugin discovery, loading, and execution.
    /// </summary>
    public class PluginRegistry : IPluginRegistry
    {
        private const string CanonicalDiscordPluginId = "discord-board";
        private const string LegacyDiscordAlliancePluginId = "discord-alliance";
        private const string LegacyDiscordAliancePluginId = "discord-aliance";

        private readonly Dictionary<string, Type> _registeredPlugins = new();
        private readonly Dictionary<int, IDiscordBotPlugin> _activePlugins = new();
        private readonly ILogger<PluginRegistry> _logger;

        /// <summary>
        /// Creates a plugin registry and registers built-in plugins.
        /// </summary>
        public PluginRegistry(ILogger<PluginRegistry> logger)
        {
            _logger = logger;
            RegisterDefaultPlugins();
        }

        /// <summary>
        /// Registers default plugins that ship with the application.
        /// </summary>
        private void RegisterDefaultPlugins()
        {
            RegisterPlugin(CanonicalDiscordPluginId, typeof(DiscordBoardPlugin.DiscordBoardPlugin));
        }

        /// <summary>
        /// Registers a plugin type for a given plugin id.
        /// </summary>
        public void RegisterPlugin(string pluginId, Type pluginType)
        {
            var normalizedPluginId = NormalizePluginId(pluginId);

            if (!typeof(IDiscordBotPlugin).IsAssignableFrom(pluginType))
            {
                throw new ArgumentException($"Plugin type {pluginType.Name} must implement IDiscordBotPlugin", nameof(pluginType));
            }

            _registeredPlugins[normalizedPluginId] = pluginType;
            _logger.LogInformation("Registered plugin: {PluginId} ({PluginType})", normalizedPluginId, pluginType.Name);
        }

        /// <summary>
        /// Returns an existing plugin instance for a bot, or creates one if missing.
        /// </summary>
        public IDiscordBotPlugin GetOrCreatePlugin(int botId, string pluginId)
        {
            var normalizedPluginId = NormalizePluginId(pluginId);

            if (_activePlugins.TryGetValue(botId, out var plugin))
            {
                return plugin;
            }

            if (!_registeredPlugins.TryGetValue(normalizedPluginId, out var pluginType))
            {
                throw new KeyNotFoundException($"Plugin '{pluginId}' not found in registry");
            }

            var instance = Activator.CreateInstance(pluginType) as IDiscordBotPlugin;
            if (instance == null)
            {
                throw new InvalidOperationException($"Failed to instantiate plugin {pluginId}");
            }

            _activePlugins[botId] = instance;
            _logger.LogInformation("Created plugin instance for bot {BotId}: {PluginId}", botId, normalizedPluginId);
            return instance;
        }

        /// <summary>
        /// Removes the cached plugin instance for a bot.
        /// </summary>
        public void RemovePlugin(int botId)
        {
            _activePlugins.Remove(botId);
            _logger.LogInformation("Removed plugin instance for bot {BotId}", botId);
        }

        /// <summary>
        /// Gets the currently cached plugin for a bot, if available.
        /// </summary>
        public IDiscordBotPlugin? GetPlugin(int botId)
        {
            _activePlugins.TryGetValue(botId, out var plugin);
            return plugin;
        }

        /// <summary>
        /// Gets all currently registered plugin ids.
        /// </summary>
        public IEnumerable<string> GetRegisteredPluginIds() => _registeredPlugins.Keys;

        /// <summary>
        /// Checks whether a plugin id is registered.
        /// </summary>
        public bool IsPluginRegistered(string pluginId) => _registeredPlugins.ContainsKey(NormalizePluginId(pluginId));

        /// <summary>
        /// Normalizes legacy plugin ids to the canonical id.
        /// </summary>
        private static string NormalizePluginId(string pluginId)
        {
            if (string.Equals(pluginId, LegacyDiscordAlliancePluginId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pluginId, LegacyDiscordAliancePluginId, StringComparison.OrdinalIgnoreCase))
            {
                return CanonicalDiscordPluginId;
            }

            return pluginId;
        }
    }
}
