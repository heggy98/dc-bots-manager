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
                TeamsMessageId = config.TeamsMessageId,
                RoleMessageId = config.RoleMessageId,
                ReactionChannelId = config.ReactionChannelId
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
                    TeamsMessageId = data.TeamsMessageId,
                    RoleMessageId = data.RoleMessageId,
                    ReactionChannelId = data.ReactionChannelId
                };
                _db.BotConfigurations.Add(config);
            }
            else
            {
                config.TeamsMessageId = data.TeamsMessageId;
                config.RoleMessageId = data.RoleMessageId;
                config.ReactionChannelId = data.ReactionChannelId;
            }

            await _db.SaveChangesAsync();
        }
    }
}
