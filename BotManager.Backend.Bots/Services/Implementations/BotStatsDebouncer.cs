using System.Collections.Concurrent;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Debounces per-bot update callbacks so rapid event bursts trigger one consolidated push.
    /// </summary>
    public sealed class BotStatsDebouncer
    {
        private static readonly ConcurrentDictionary<int, CancellationTokenSource> Pending = new();

        private readonly TimeSpan _window;
        private readonly Func<int, CancellationToken, Task> _onElapsed;

        public BotStatsDebouncer(TimeSpan window, Func<int, CancellationToken, Task> onElapsed)
        {
            _window = window;
            _onElapsed = onElapsed;
        }

        public void Schedule(int botId)
        {
            var cts = new CancellationTokenSource();
            Pending.AddOrUpdate(botId, _ => cts, (_, old) =>
            {
                old.Cancel();
                old.Dispose();
                return cts;
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_window, cts.Token);
                    await _onElapsed(botId, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Superseded by a newer event.
                }
                finally
                {
                    if (Pending.TryGetValue(botId, out var current) && ReferenceEquals(current, cts))
                    {
                        Pending.TryRemove(botId, out _);
                    }

                    cts.Dispose();
                }
            });
        }
    }
}