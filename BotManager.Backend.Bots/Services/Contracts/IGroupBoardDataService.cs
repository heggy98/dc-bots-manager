using BotManager.Backend.Bots.Models;

namespace BotManager.Backend.Bots.Services.Contracts
{
    public interface IGroupBoardDataService
    {
        Task<BoardGroupCollectionDto> GetAsync(int botId);
        Task SaveAsync(int botId, BoardGroupCollectionDto data);
    }
}