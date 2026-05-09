using System.Text.Json;
using BotManager.Api.Models;

namespace BotManager.Api.Services
{
    public class FileTeamsDataService : ITeamsDataService
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
    }
}
