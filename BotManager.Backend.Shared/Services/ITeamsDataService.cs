using BotManager.Backend.Shared.Models;

namespace BotManager.Backend.Shared.Services
{
    public interface ITeamsDataService
    {
        Task<BotTeamsDto> GetAsync(int botId);
        Task SaveAsync(int botId, BotTeamsDto data);
    }
}
