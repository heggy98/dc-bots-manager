using BotManager.Backend.Bots.Models;

namespace BotManager.Backend.Bots.Services.Contracts
{
    /// <summary>
    /// Defines storage operations for board-group DTO collections.
    /// </summary>
    public interface IGroupBoardDataService
    {
        /// <summary>
        /// Loads board groups for a specific bot.
        /// </summary>
        Task<BoardGroupCollectionDto> GetAsync(int botId, int? boardConfigurationId = null);

        /// <summary>
        /// Saves board groups for a specific bot.
        /// </summary>
        Task SaveAsync(int botId, BoardGroupCollectionDto data, int? boardConfigurationId = null);
    }
}