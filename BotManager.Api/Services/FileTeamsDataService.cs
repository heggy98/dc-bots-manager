using System.Text.Json;
using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Contracts.Models;
using BotManager.Backend.Contracts.Services;

namespace BotManager.Api.Services
{
    public class FileTeamsDataService : ITeamsDataService, IGroupDataService
    {
        public async Task<BotTeamsDto> GetAsync(int botId)
        {
            var result = new BotTeamsDto();
            var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "discord-bot-aliance");
            var teamsPath = Path.Combine(basePath, "teams.json");

            if (File.Exists(teamsPath))
            {
                var teamsJson = await File.ReadAllTextAsync(teamsPath);
                // Deserialize directly to TeamDto list
                result.Teams = JsonSerializer.Deserialize<List<TeamDto>>(teamsJson) ?? new List<TeamDto>();
            }

            return result;
        }

        public async Task SaveAsync(int botId, BotTeamsDto data)
        {
            var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "discord-bot-aliance");
            var teamsPath = Path.Combine(basePath, "teams.json");
            var options = new JsonSerializerOptions { WriteIndented = true };

            var teamsJson = JsonSerializer.Serialize(data.Teams, options);
            await File.WriteAllTextAsync(teamsPath, teamsJson);
        }

        async Task<BotGroupsDto> IGroupDataService.GetAsync(int botId)
        {
            var teams = await GetAsync(botId);
            return GroupContractMapper.FromTeams(teams);
        }

        async Task IGroupDataService.SaveAsync(int botId, BotGroupsDto data)
        {
            await SaveAsync(botId, GroupContractMapper.ToTeams(data));
        }
    }
}
