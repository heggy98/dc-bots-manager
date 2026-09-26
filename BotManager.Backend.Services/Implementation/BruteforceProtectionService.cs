using BotManager.Backend.Services.Interfaces;
using System.Collections.Concurrent;

namespace BotManager.Backend.Services.Implementation
{
    /// <summary>
    /// Tracks failed login attempts and temporary lockouts per key (IP address or account).
    /// </summary>
    public class BruteforceProtectionService : IBruteforceProtectionService
    {
        /// <summary>Failed attempts older than this window (and not locked) are forgotten.</summary>
        private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);

        /// <summary>Stale entries are swept at most once per this interval.</summary>
        private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

        private readonly ConcurrentDictionary<string, AttemptEntry> _failedAttempts = new();
        private readonly Func<DateTime> _utcNow;
        private long _lastSweepTicks;

        /// <summary>
        /// Creates a new bruteforce protection service.
        /// </summary>
        public BruteforceProtectionService() : this(() => DateTime.UtcNow) { }

        /// <summary>
        /// Creates a new bruteforce protection service with a custom clock (for tests).
        /// </summary>
        public BruteforceProtectionService(Func<DateTime> utcNow)
        {
            _utcNow = utcNow;
            _lastSweepTicks = utcNow().Ticks;
        }

        /// <summary>
        /// Returns whether the given key is currently locked out.
        /// </summary>
        public bool IsLocked(string key)
        {
            var now = _utcNow();
            if (!_failedAttempts.TryGetValue(key, out var entry))
                return false;

            if (entry.LockedUntil.HasValue)
            {
                if (entry.LockedUntil.Value > now)
                    return true;

                _failedAttempts.TryRemove(new KeyValuePair<string, AttemptEntry>(key, entry));
            }

            return false;
        }

        /// <summary>
        /// Gets the number of failed attempts currently tracked for a key.
        /// </summary>
        public int GetFailedAttempts(string key)
        {
            if (!_failedAttempts.TryGetValue(key, out var entry))
                return 0;

            return IsExpired(entry, _utcNow()) ? 0 : entry.Count;
        }

        /// <summary>
        /// Registers a failed attempt and applies lockout when threshold is reached.
        /// </summary>
        public void RegisterFailure(string key, int maxAttempts, int lockoutMinutes)
        {
            maxAttempts = Math.Max(1, maxAttempts);
            lockoutMinutes = Math.Max(1, lockoutMinutes);
            var now = _utcNow();

            _failedAttempts.AddOrUpdate(key,
                _ => Next(new AttemptEntry(0, null, now), now, maxAttempts, lockoutMinutes),
                (_, existing) => Next(IsExpired(existing, now) ? new AttemptEntry(0, null, now) : existing,
                    now, maxAttempts, lockoutMinutes));

            SweepIfDue(now);
        }

        /// <summary>
        /// Registers a failed attempt using default lockout settings.
        /// </summary>
        public void RegisterFailure(string key)
        {
            RegisterFailure(key, 5, 5);
        }

        /// <summary>
        /// Clears failed-attempt tracking for a successful login.
        /// </summary>
        public void RegisterSuccess(string key)
        {
            _failedAttempts.TryRemove(key, out _);
        }

        /// <summary>Number of tracked keys (for diagnostics/tests).</summary>
        public int TrackedKeyCount => _failedAttempts.Count;

        private static AttemptEntry Next(AttemptEntry existing, DateTime now, int maxAttempts, int lockoutMinutes)
        {
            var newCount = existing.Count + 1;
            var lockedUntil = newCount >= maxAttempts ? now.AddMinutes(lockoutMinutes) : existing.LockedUntil;
            return new AttemptEntry(newCount, lockedUntil, now);
        }

        private static bool IsExpired(AttemptEntry entry, DateTime now)
        {
            if (entry.LockedUntil.HasValue)
                return entry.LockedUntil.Value <= now;

            return now - entry.LastAttemptAt > AttemptWindow;
        }

        private void SweepIfDue(DateTime now)
        {
            var last = Interlocked.Read(ref _lastSweepTicks);
            if (now.Ticks - last < SweepInterval.Ticks)
                return;

            if (Interlocked.CompareExchange(ref _lastSweepTicks, now.Ticks, last) != last)
                return;

            foreach (var pair in _failedAttempts)
            {
                if (IsExpired(pair.Value, now))
                    _failedAttempts.TryRemove(pair);
            }
        }

        private readonly record struct AttemptEntry(int Count, DateTime? LockedUntil, DateTime LastAttemptAt);
    }
}
