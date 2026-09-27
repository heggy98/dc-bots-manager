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

    [Fact]
    public async Task Schedule_CallbackThrows_FirstFailureDoesNotBlockLaterSchedules()
    {
        var invocationCount = 0;

        var debouncer = new BotStatsDebouncer(
            TimeSpan.FromMilliseconds(40),
            (botId, token) =>
            {
                var current = Interlocked.Increment(ref invocationCount);
                if (current == 1)
                {
                    throw new InvalidOperationException("Simulated callback failure");
                }

                return Task.CompletedTask;
            });

        // Poll instead of fixed sleeps so the test is stable on busy CI machines.
        debouncer.Schedule(4);
        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref invocationCount) >= 1));

        debouncer.Schedule(4);
        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref invocationCount) >= 2));

        // No extra invocations afterwards.
        await Task.Delay(120);
        Assert.Equal(2, Volatile.Read(ref invocationCount));
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }

    [Fact]
    public async Task Schedule_CallbackThrowsForOneBot_DoesNotBlockOtherBot()
    {
        var seenBot2 = false;

        var debouncer = new BotStatsDebouncer(
            TimeSpan.FromMilliseconds(35),
            (botId, token) =>
            {
                if (botId == 1)
                {
                    throw new InvalidOperationException("Simulated bot1 failure");
                }

                if (botId == 2)
                {
                    seenBot2 = true;
                }

                return Task.CompletedTask;
            });

        debouncer.Schedule(1);
        debouncer.Schedule(2);

        await Task.Delay(220);

        Assert.True(seenBot2);
    }
}