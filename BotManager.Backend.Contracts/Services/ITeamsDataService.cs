using BotManager.Backend.Contracts.Models;

namespace BotManager.Backend.Contracts.Services
{
    public interface ITeamsDataService
    {
        Task<BotTeamsDto> GetAsync(int botId);
        Task SaveAsync(int botId, BotTeamsDto data);
    }
}
