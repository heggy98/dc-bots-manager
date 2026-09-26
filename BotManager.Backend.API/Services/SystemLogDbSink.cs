using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Serilog.Core;
using Serilog.Events;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Serilog batched sink that writes log events to the SystemLogs table through EF Core,
    /// so it works with every supported database provider (SQL Server, PostgreSQL).
    /// </summary>
    public sealed class SystemLogDbSink : IBatchedLogEventSink
    {
        private readonly IServiceProvider _services;

        /// <summary>
        /// Creates the sink. Each batch uses its own DI scope/DbContext.
        /// </summary>
        public SystemLogDbSink(IServiceProvider services)
        {
            _services = services;
        }

        /// <summary>
        /// Returns whether an event should be persisted: warnings/errors plus bot-scoped events,
        /// never EF Core's own events (they would recurse through this sink).
        /// </summary>
        public static bool ShouldPersist(LogEvent logEvent)
        {
            if (logEvent.Properties.TryGetValue("SourceContext", out var source)
                && source is ScalarValue { Value: string context }
                && context.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            {
                return false;
            }

            return logEvent.Level >= LogEventLevel.Warning || logEvent.Properties.ContainsKey("BotId");
        }

        /// <inheritdoc />
        public async Task EmitBatchAsync(IReadOnlyCollection<LogEvent> batch)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();
            db.SystemLogs.AddRange(batch.Select(ToEntity));
            await db.SaveChangesAsync();
        }

        /// <inheritdoc />
        public Task OnEmptyBatchAsync() => Task.CompletedTask;

        /// <summary>
        /// Maps a log event to a SystemLog row, truncating to the column sizes.
        /// </summary>
        public static SystemLog ToEntity(LogEvent logEvent) => new()
        {
            Timestamp = logEvent.Timestamp.UtcDateTime,
            Level = Truncate(logEvent.Level.ToString(), 50)!,
            Category = Truncate(GetString(logEvent, "SourceContext"), 500),
            Message = Truncate(logEvent.RenderMessage(), 4000) ?? string.Empty,
            Exception = Truncate(logEvent.Exception?.ToString(), 4000),
            BotId = GetInt(logEvent, "BotId")
        };

        private static string? GetString(LogEvent logEvent, string name)
            => logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue { Value: string s } ? s : null;

        private static int? GetInt(LogEvent logEvent, string name)
        {
            if (!logEvent.Properties.TryGetValue(name, out var value) || value is not ScalarValue scalar)
            {
                return null;
            }

            return scalar.Value switch
            {
                int i => i,
                long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => null
            };
        }

        private static string? Truncate(string? value, int maxLength)
            => value == null || value.Length <= maxLength ? value : value[..maxLength];
    }
}
