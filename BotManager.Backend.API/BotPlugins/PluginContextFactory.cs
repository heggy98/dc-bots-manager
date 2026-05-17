using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Shared.Models;
using BotManager.Backend.Shared.Services;
using Microsoft.Extensions.Logging;

namespace BotManager.Backend.API.BotPlugins
{
    /// <summary>
    /// Creates plugin context objects from scoped application services.
    /// </summary>
    public class PluginContextFactory : IPluginContextFactory
    {
        private readonly ISystemConfigService _systemConfigService;
        private readonly ITeamsDataService _teamsDataService;
        private readonly IBotDataService _botDataService;

        /// <summary>
        /// Creates a new plugin context factory.
        /// </summary>
        public PluginContextFactory(
            ISystemConfigService systemConfigService,
            ITeamsDataService teamsDataService,
            IBotDataService botDataService)
        {
            _systemConfigService = systemConfigService;
            _teamsDataService = teamsDataService;
            _botDataService = botDataService;
        }

        /// <summary>
        /// Builds a plugin context for a single bot execution scope.
        /// </summary>
        public IPluginContext Create(
            Bot bot,
            BotManagerDbContext dbContext,
            ILogger logger,
            IServiceProvider serviceProvider)
        {
            return new PluginContext
            {
                Bot = bot,
                DbContext = dbContext,
                SystemConfigService = _systemConfigService,
                TeamsDataService = _teamsDataService,
                BotDataService = _botDataService,
                Logger = logger,
                ServiceProvider = serviceProvider
            };
        }
    }
}
