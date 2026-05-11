using System.Text.Json;
using BotManager.Backend.Contracts.Models;
using BotManager.Backend.Contracts.Services;

namespace BotManager.Api.Services
{
    public class FileBotDataService : IBotDataService
    {
        public async Task<BotConfigurationDto> GetAsync(int botId)
        {
            var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "discord-bot-aliance");
            var botDataPath = Path.Combine(basePath, "botdata.json");

            if (!File.Exists(botDataPath))
            {
                return new BotConfigurationDto();
            }

            var json = await File.ReadAllTextAsync(botDataPath);
            return JsonSerializer.Deserialize<BotConfigurationDto>(json) ?? new BotConfigurationDto();
        }

        public async Task SaveAsync(int botId, BotConfigurationDto data)
        {
            var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "discord-bot-aliance");
            var botDataPath = Path.Combine(basePath, "botdata.json");
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(data, options);
            await File.WriteAllTextAsync(botDataPath, json);
        }
    }
}
