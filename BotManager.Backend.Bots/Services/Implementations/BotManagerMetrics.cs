using System.Diagnostics.Metrics;
using BotManager.Backend.Bots.Services.Contracts;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Custom application metrics (meter <see cref="MeterName"/>), exported by OpenTelemetry
    /// (Prometheus <c>/metrics</c> and/or OTLP) when enabled in the API host.
    /// </summary>
    public sealed class BotManagerMetrics
    {
        /// <summary>Name of the meter holding all BotManager instruments.</summary>
        public const string MeterName = "BotManager";

        private readonly Meter _meter;
        private readonly Counter<long> _commandsExecuted;
        private readonly Counter<long> _botDisconnects;
        private readonly Counter<long> _alertsSent;
        private int _runtimeObserved;

        /// <summary>
        /// Creates the meter and its instruments.
        /// </summary>
        public BotManagerMetrics(IMeterFactory meterFactory)
        {
            _meter = meterFactory.Create(MeterName);
            // Counters are exported with a "_total" suffix by the Prometheus exporter.
            _commandsExecuted = _meter.CreateCounter<long>("commands_executed",
                description: "Slash commands executed, by command name and success.");
            _botDisconnects = _meter.CreateCounter<long>("bot_disconnects",
                description: "Unexpected Discord gateway disconnects, by bot.");
            _alertsSent = _meter.CreateCounter<long>("alerts_sent",
                description: "Bot alerts delivered (or attempted), by kind, channel and success.");
        }

        /// <summary>
        /// Registers the observable gauges backed by the Discord runtime (called once by the runtime service).
        /// </summary>
        /// <param name="runningBots">Returns the number of connected bots.</param>
        /// <param name="gatewayLatencies">Returns the gateway latency (ms) of every connected bot, tagged with bot_id.</param>
        internal void ObserveRuntime(Func<int> runningBots, Func<IEnumerable<Measurement<int>>> gatewayLatencies)
        {
            if (Interlocked.Exchange(ref _runtimeObserved, 1) == 1)
            {
                return;
            }

            _meter.CreateObservableGauge("bots_running", runningBots,
                description: "Number of bots currently connected to the Discord gateway.");
            _meter.CreateObservableGauge("gateway_latency_ms", gatewayLatencies,
                description: "Discord gateway heartbeat latency in milliseconds, by bot.");
        }

        /// <summary>Counts an executed slash command.</summary>
        public void RecordCommandExecuted(string command, bool success)
            => _commandsExecuted.Add(1,
                new KeyValuePair<string, object?>("command", command),
                new KeyValuePair<string, object?>("success", success ? "true" : "false"));

        /// <summary>Counts an unexpected gateway disconnect of a bot.</summary>
        public void RecordDisconnect(int botId)
            => _botDisconnects.Add(1, new KeyValuePair<string, object?>("bot_id", botId.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        /// <summary>Counts an alert delivery attempt on one channel.</summary>
        public void RecordAlertSent(BotAlertKind kind, string channel, bool success)
            => _alertsSent.Add(1,
                new KeyValuePair<string, object?>("kind", kind.ToString()),
                new KeyValuePair<string, object?>("channel", channel),
                new KeyValuePair<string, object?>("success", success ? "true" : "false"));
    }
}
