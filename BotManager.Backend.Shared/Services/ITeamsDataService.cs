using BotManager.Backend.Shared.Models;

namespace BotManager.Backend.Shared.Services
{
    /// <summary>
    /// Defines teams data storage operations.
    /// </summary>
    public interface ITeamsDataService
    {
        /// <summary>
        /// Loads teams data for a specific bot.
        /// </summary>
        Task<BotTeamsDto> GetAsync(int botId, int? boardConfigurationId = null);

        /// <summary>
        /// Saves teams data for a specific bot.
        /// </summary>
        Task SaveAsync(int botId, BotTeamsDto data, int? boardConfigurationId = null);
    }
}
