using BotManager.Backend.Contracts.Models;

namespace BotManager.Backend.Contracts.Services
{
    public interface IGroupDataService
    {
        Task<BotGroupsDto> GetAsync(int botId);
        Task SaveAsync(int botId, BotGroupsDto data);
    }
}