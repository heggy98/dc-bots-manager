using BotManager.Backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Services
{
    public class SystemConfigService
    {
        private readonly BotManagerDbContext _db;

        public SystemConfigService(BotManagerDbContext db) { _db = db; }

        public async Task<string?> GetValueAsync(string key)
        {
            var cfg = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key);
            return cfg?.Value;
        }

        public async Task<int> GetIntAsync(string key, int defaultValue)
        {
            var val = await GetValueAsync(key);
            return int.TryParse(val, out var result) ? result : defaultValue;
        }

        public async Task SetValueAsync(string key, string value)
        {
            var cfg = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key);
            if (cfg == null)
            {
                _db.SystemConfigs.Add(new Backend.Entities.Entities.SystemConfig { Key = key, Value = value });
            }
            else
            {
                cfg.Value = value;
            }
            await _db.SaveChangesAsync();
        }

        public async Task<List<Backend.Entities.Entities.SystemConfig>> GetAllAsync()
            => await _db.SystemConfigs.OrderBy(c => c.Key).ToListAsync();
    }
}
