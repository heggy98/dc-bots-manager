using BotManager.Backend.Bots.Services.Implementations;
using Xunit;

namespace BotManager.Backend.Bots.Tests;

public class BotStatsDebouncerTests
{
    [Fact]
    public async Task Schedule_SameBotRapidBurst_ExecutesCallbackOnce()
    {
        var count = 0;
        var debouncer = new BotStatsDebouncer(
            TimeSpan.FromMilliseconds(50),
            (botId, token) =>
            {
                Interlocked.Increment(ref count);
                return Task.CompletedTask;
            });

        debouncer.Schedule(7);
        debouncer.Schedule(7);
        debouncer.Schedule(7);

        await Task.Delay(250);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Schedule_DifferentBots_ExecutesPerBot()
    {
        var seenBots = new HashSet<int>();
        var gate = new object();

        var debouncer = new BotStatsDebouncer(
            TimeSpan.FromMilliseconds(40),
            (botId, token) =>
            {
                lock (gate)
                {
                    seenBots.Add(botId);
                }

                return Task.CompletedTask;
            });

        debouncer.Schedule(1);
        debouncer.Schedule(2);

        await Task.Delay(220);

        lock (gate)
        {
            Assert.Contains(1, seenBots);
            Assert.Contains(2, seenBots);
            Assert.Equal(2, seenBots.Count);
        }
    }

    [Fact]
    public async Task Schedule_RescheduleBeforeWindow_CancelsPreviousPendingCallback()
    {
        var count = 0;
        var debouncer = new BotStatsDebouncer(
            TimeSpan.FromMilliseconds(80),
            (botId, token) =>
            {
                Interlocked.Increment(ref count);
                return Task.CompletedTask;
            });

        debouncer.Schedule(9);
        await Task.Delay(30);
        debouncer.Schedule(9);

        await Task.Delay(260);
        Assert.Equal(1, count);
    }
}