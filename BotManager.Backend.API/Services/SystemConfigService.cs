using BotManager.Backend.Entities;
using BotManager.Backend.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Provides database-backed operations for system configuration values.
    /// </summary>
    public class SystemConfigService : ISystemConfigService
    {
        private readonly BotManagerDbContext _db;

        /// <summary>
        /// Creates a new system configuration service.
        /// </summary>
        public SystemConfigService(BotManagerDbContext db) { _db = db; }

        /// <summary>
        /// Gets a configuration value by key.
        /// </summary>
        public async Task<string?> GetValueAsync(string key)
        {
            var cfg = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key);
            return cfg?.Value;
        }

        /// <summary>
        /// Gets an integer configuration value or returns a fallback value.
        /// </summary>
        public async Task<int> GetIntAsync(string key, int defaultValue)
        {
            var val = await GetValueAsync(key);
            return int.TryParse(val, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Sets a configuration value, creating the row if it does not exist.
        /// </summary>
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

        /// <summary>
        /// Returns all configuration rows ordered by key.
        /// </summary>
        public async Task<List<Backend.Entities.Entities.SystemConfig>> GetAllAsync()
            => await _db.SystemConfigs.OrderBy(c => c.Key).ToListAsync();
    }
}
