using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Resolves the board plugin of a bot session, builds its per-event context and
    /// initializes it exactly once per session start.
    /// </summary>
    internal sealed class BoardPluginHost : IDisposable
    {
        /// <summary>
        /// Plugin id of the Discord board plugin every session dispatches to.
        /// </summary>
        internal const string BoardPluginId = "discord-board";

        private readonly ILogger _logger;
        private readonly IPluginRegistry _pluginRegistry;
        private readonly SemaphoreSlim _pluginInitLock = new(1, 1);
        private volatile bool _pluginInitialized = false;

        /// <summary>
        /// Creates a new plugin host.
        /// </summary>
        public BoardPluginHost(ILogger logger, IPluginRegistry pluginRegistry)
        {
            _logger = logger;
            _pluginRegistry = pluginRegistry;
        }

        /// <summary>
        /// Marks the plugin as not initialized so the next event re-runs plugin initialization.
        /// </summary>
        public void ResetInitialization()
        {
            _pluginInitialized = false;
        }

        /// <summary>
        /// Prepares plugin and plugin context for command, component and reaction dispatch operations.
        /// </summary>
        public PluginSetupResult TryPreparePluginContext(
            IServiceProvider serviceProvider,
            Bot bot,
            BotManagerDbContext db,
            string commandName)
        {
            try
            {
                var plugin = _pluginRegistry.GetOrCreatePlugin(bot.BotId, BoardPluginId);
                if (plugin == null)
                {
                    _logger.LogError("Plugin instance creation failed for bot {BotId}. Command={CommandName}", bot.BotId, commandName);
                    return PluginSetupResult.Fail("Nepodařilo se načíst plugin.");
                }

                var pluginContextFactory = serviceProvider.GetRequiredService<IPluginContextFactory>();
                var pluginContext = pluginContextFactory.Create(bot, db, _logger, serviceProvider);

                return PluginSetupResult.Ok(plugin, pluginContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to prepare slash plugin context for command {CommandName}", commandName);
                return PluginSetupResult.Fail("Chyba při přípravě plugin kontextu.");
            }
        }

        /// <summary>
        /// Initializes the plugin exactly once, even when several handlers race on the first event.
        /// </summary>
        public async Task EnsurePluginInitializedAsync(IDiscordBotPlugin plugin, IPluginContext pluginContext)
        {
            if (_pluginInitialized)
            {
                return;
            }

            await _pluginInitLock.WaitAsync();
            try
            {
                if (!_pluginInitialized)
                {
                    await plugin.InitializeAsync(pluginContext);
                    _pluginInitialized = true;
                }
            }
            finally
            {
                _pluginInitLock.Release();
            }
        }

        /// <summary>
        /// Releases the initialization lock.
        /// </summary>
        public void Dispose()
        {
            _pluginInitLock.Dispose();
        }
    }

    /// <summary>
    /// Result envelope for plugin setup and contextual error messaging.
    /// </summary>
    internal sealed record PluginSetupResult(bool Success, IDiscordBotPlugin? Plugin, IPluginContext? PluginContext, string UserMessage)
    {
        /// <summary>
        /// Creates a successful plugin setup result.
        /// </summary>
        public static PluginSetupResult Ok(IDiscordBotPlugin plugin, IPluginContext pluginContext)
            => new(true, plugin, pluginContext, string.Empty);

        /// <summary>
        /// Creates a failed plugin setup result with a user-facing message.
        /// </summary>
        public static PluginSetupResult Fail(string userMessage)
            => new(false, null, null, userMessage);
    }
}
