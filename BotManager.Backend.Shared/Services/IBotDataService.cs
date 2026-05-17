using BotManager.Backend.Shared.Models;

namespace BotManager.Backend.Shared.Services
{
    /// <summary>
    /// Defines bot configuration storage operations.
    /// </summary>
    public interface IBotDataService
    {
        /// <summary>
        /// Loads bot configuration data for a specific bot.
        /// </summary>
        Task<BotConfigurationDto> GetAsync(int botId);

        /// <summary>
        /// Saves bot configuration data for a specific bot.
        /// </summary>
        Task SaveAsync(int botId, BotConfigurationDto data);
    }
}
