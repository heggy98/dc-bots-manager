using BotManager.Backend.Shared.Models;

namespace BotManager.Backend.Shared.Services
{
    public interface IBotDataService
    {
        Task<BotConfigurationDto> GetAsync(int botId);
        Task SaveAsync(int botId, BotConfigurationDto data);
    }
}
