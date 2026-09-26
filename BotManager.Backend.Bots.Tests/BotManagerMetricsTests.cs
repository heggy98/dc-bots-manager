using System.Diagnostics.Metrics;
using BotManager.Backend.Bots.Services.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BotManager.Backend.Bots.Tests;

public class BotManagerMetricsTests
{
    [Fact]
    public void ObserveRuntime_PublishesRunningBotsAndLatencyPerBot()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var metrics = new BotManagerMetrics(services.GetRequiredService<IMeterFactory>());
        metrics.ObserveRuntime(
            () => 2,
            () => [new Measurement<int>(42, new KeyValuePair<string, object?>("bot_id", "1")),
                   new Measurement<int>(99, new KeyValuePair<string, object?>("bot_id", "2"))]);

        var seen = new List<(string Name, int Value, string? BotId)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == BotManagerMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
        {
            string? botId = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "bot_id") botId = tag.Value as string;
            }
            seen.Add((instrument.Name, value, botId));
        });
        listener.Start();
        listener.RecordObservableInstruments();

        Assert.Contains(("bots_running", 2, null), seen);
        Assert.Contains(("gateway_latency_ms", 42, "1"), seen);
        Assert.Contains(("gateway_latency_ms", 99, "2"), seen);
    }
}
