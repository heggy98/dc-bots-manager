using BotManager.Api.Models;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Api.Services
{
    public class DbBotDataService : IBotDataService
    {
        private readonly BotManagerDbContext _db;

        public DbBotDataService(BotManagerDbContext db)
        {
            _db = db;
        }

        public async Task<BotConfigurationDto> GetAsync(int botId)
        {
            var config = await _db.BotConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.BotId == botId);

            if (config == null)
            {
                return new BotConfigurationDto();
            }

            return new BotConfigurationDto
            {
                BoardChannelId = config.BoardChannelId?.ToString(),
                BoardMessageId = config.BoardMessageId?.ToString()
            };
        }

        public async Task SaveAsync(int botId, BotConfigurationDto data)
        {
            var config = await _db.BotConfigurations.FirstOrDefaultAsync(c => c.BotId == botId);

            if (config == null)
            {
                config = new BotConfiguration
                {
                    BotId = botId,
                    BoardChannelId = ulong.TryParse(data.BoardChannelId, out var chId) ? chId : null,
                    BoardMessageId = ulong.TryParse(data.BoardMessageId, out var msgId) ? msgId : null
                };
                _db.BotConfigurations.Add(config);
            }
            else
            {
                config.BoardChannelId = ulong.TryParse(data.BoardChannelId, out var chId) ? chId : null;
                config.BoardMessageId = ulong.TryParse(data.BoardMessageId, out var msgId) ? msgId : null;
            }

            await _db.SaveChangesAsync();
        }
    }
}
