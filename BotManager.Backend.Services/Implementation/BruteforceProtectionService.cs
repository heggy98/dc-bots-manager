using BotManager.Backend.Services.Interfaces;
using System.Collections.Concurrent;

namespace BotManager.Backend.Services.Implementation
{
    public class BruteforceProtectionService : IBruteforceProtectionService
    {
        private readonly ConcurrentDictionary<string, (int Count, DateTime? LockedUntil)> _failedAttempts = new();

        public bool IsLocked(string ipAddress)
        {
            if (_failedAttempts.TryGetValue(ipAddress, out var entry))
            {
                if (entry.LockedUntil.HasValue && entry.LockedUntil.Value > DateTime.UtcNow)
                    return true;
                if (entry.LockedUntil.HasValue && entry.LockedUntil.Value <= DateTime.UtcNow)
                {
                    _failedAttempts.TryRemove(ipAddress, out _);
                    return false;
                }
            }
            return false;
        }

        public int GetFailedAttempts(string ipAddress)
        {
            return _failedAttempts.TryGetValue(ipAddress, out var entry) ? entry.Count : 0;
        }

        public void RegisterFailure(string ipAddress, int maxAttempts = 5, int lockoutMinutes = 5)
        {
            _failedAttempts.AddOrUpdate(ipAddress,
                (1, null),
                (key, existing) =>
                {
                    var newCount = existing.Count + 1;
                    if (newCount >= maxAttempts)
                        return (newCount, DateTime.UtcNow.AddMinutes(lockoutMinutes));
                    return (newCount, existing.LockedUntil);
                });
        }

        public void RegisterFailure(string ipAddress)
        {
            RegisterFailure(ipAddress, 5, 5);
        }

        public void RegisterSuccess(string ipAddress)
        {
            _failedAttempts.TryRemove(ipAddress, out _);
        }
    }
}
