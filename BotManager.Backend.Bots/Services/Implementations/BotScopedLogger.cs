using Microsoft.Extensions.Logging;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Logger decorator that attaches a structured "BotId" property to every event, so runtime logs
    /// can be filtered per bot (SystemLogs.BotId) instead of by searching message text.
    /// </summary>
    internal sealed class BotScopedLogger : ILogger
    {
        private readonly ILogger _inner;
        private readonly IReadOnlyDictionary<string, object> _scope;

        public BotScopedLogger(ILogger inner, int botId)
        {
            _inner = inner;
            _scope = new Dictionary<string, object> { ["BotId"] = botId };
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!_inner.IsEnabled(logLevel))
            {
                return;
            }

            using (_inner.BeginScope(_scope))
            {
                _inner.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
