using BotManager.Backend.Contracts.Models;

namespace BotManager.Backend.Contracts.Services
{
    public interface IBotDataService
    {
        Task<BotConfigurationDto> GetAsync(int botId);
        Task SaveAsync(int botId, BotConfigurationDto data);
    }
}
