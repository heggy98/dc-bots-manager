using BotManager.Backend.Shared.Models;

namespace BotManager.Backend.Shared.Services
{
    public interface IGroupDataService
    {
        Task<BotGroupsDto> GetAsync(int botId);
        Task SaveAsync(int botId, BotGroupsDto data);
    }
}