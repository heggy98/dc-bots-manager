using BotManager.Backend.Entities.Entities;

namespace BotManager.Backend.Shared.Services
{
    /// <summary>
    /// Service for managing system-wide configuration values
    /// </summary>
    public interface ISystemConfigService
    {
        /// <summary>
        /// Get a configuration value by key
        /// </summary>
        Task<string?> GetValueAsync(string key);

        /// <summary>
        /// Get an integer configuration value with a default fallback
        /// </summary>
        Task<int> GetIntAsync(string key, int defaultValue);

        /// <summary>
        /// Set a configuration value
        /// </summary>
        Task SetValueAsync(string key, string value);

        /// <summary>
        /// Get all configuration values
        /// </summary>
        Task<List<SystemConfig>> GetAllAsync();
    }
}
