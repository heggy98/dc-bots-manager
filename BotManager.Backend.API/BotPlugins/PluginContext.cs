using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.API.Services;
using BotManager.Backend.Shared.Services;
using Microsoft.Extensions.Logging;

namespace BotManager.Backend.API.BotPlugins
{
    /// <summary>
    /// Context provided to bot plugins, containing access to bot data, services, and databases.
    /// </summary>
    public class PluginContext
    {
        /// <summary>
        /// The bot entity this plugin is running for
        /// </summary>
        public required Bot Bot { get; set; }

        /// <summary>
        /// Database context for accessing bot-related data
        /// </summary>
        public required BotManagerDbContext DbContext { get; set; }

        /// <summary>
        /// Service for managing bot configuration
        /// </summary>
        public required SystemConfigService SystemConfigService { get; set; }

        /// <summary>
        /// Service for managing teams (loaded from file or database based on configuration)
        /// </summary>
        public required ITeamsDataService TeamsDataService { get; set; }

        /// <summary>
        /// Service for managing bot-specific data
        /// </summary>
        public required IBotDataService BotDataService { get; set; }

        /// <summary>
        /// Logger for the bot instance
        /// </summary>
        public required ILogger Logger { get; set; }

        /// <summary>
        /// Service provider for dependency injection
        /// </summary>
        public required IServiceProvider ServiceProvider { get; set; }
    }
}
