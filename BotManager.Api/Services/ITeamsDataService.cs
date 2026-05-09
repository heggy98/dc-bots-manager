using BotManager.Api.Models;

namespace BotManager.Api.Services
{
    public interface ITeamsDataService
    {
        Task<BotTeamsDto> GetAsync(int botId);
        Task SaveAsync(int botId, BotTeamsDto data);
    }
}
