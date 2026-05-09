using BotManager.Api.Models;

namespace BotManager.Api.Services
{
    public interface IBotDataService
    {
        Task<BotConfigurationDto> GetAsync(int botId);
        Task SaveAsync(int botId, BotConfigurationDto data);
    }
}
