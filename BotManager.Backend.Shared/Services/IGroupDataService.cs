using BotManager.Backend.Shared.Models;

namespace BotManager.Backend.Shared.Services
{
    /// <summary>
    /// Defines group data storage operations for bot-managed teams.
    /// </summary>
    public interface IGroupDataService
    {
        /// <summary>
        /// Loads group data for a specific bot.
        /// </summary>
        Task<BotGroupsDto> GetAsync(int botId, int? boardConfigurationId = null);

        /// <summary>
        /// Saves group data for a specific bot.
        /// </summary>
        Task SaveAsync(int botId, BotGroupsDto data, int? boardConfigurationId = null);
    }
}