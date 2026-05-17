using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Shared.Services;
using Microsoft.Extensions.Logging;

namespace BotManager.Backend.Shared.Models
{
    /// <summary>
    /// Context provided to bot plugins, containing access to bot data, services, and databases.
    /// </summary>
    public interface IPluginContext
    {
        /// <summary>
        /// The bot entity this plugin is running for
        /// </summary>
        Bot Bot { get; }

        /// <summary>
        /// Database context for accessing bot-related data
        /// </summary>
        BotManagerDbContext DbContext { get; }

        /// <summary>
        /// Service for managing bot configuration
        /// </summary>
        ISystemConfigService SystemConfigService { get; }

        /// <summary>
        /// Service for managing teams (loaded from file or database based on configuration)
        /// </summary>
        ITeamsDataService TeamsDataService { get; }

        /// <summary>
        /// Service for managing bot-specific data
        /// </summary>
        IBotDataService BotDataService { get; }

        /// <summary>
        /// Logger for the bot instance
        /// </summary>
        ILogger Logger { get; }

        /// <summary>
        /// Service provider for dependency injection
        /// </summary>
        IServiceProvider ServiceProvider { get; }
    }
}
