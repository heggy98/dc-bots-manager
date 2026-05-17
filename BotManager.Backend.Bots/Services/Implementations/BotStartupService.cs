namespace BotManager.Backend.Bots.Services.Implementations
{
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Starts and stops process-based bot manager with host lifecycle events.
    /// </summary>
    public class BotStartupService : IHostedService
    {
        private readonly BotManagerService _botManager;
        private readonly ILogger<BotStartupService> _logger;

        /// <summary>
        /// Creates a new hosted startup service for bot manager orchestration.
        /// </summary>
        public BotStartupService(BotManagerService botManager, ILogger<BotStartupService> logger)
        {
            _botManager = botManager;
            _logger = logger;
        }

        /// <summary>
        /// Starts bot manager when the host starts.
        /// </summary>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Spouštění BotManager při startu aplikace.");
            _botManager.StartBot();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Stops bot manager when the host stops.
        /// </summary>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Zastavování BotManager při ukončení aplikace.");
            _botManager.StopBot();
            return Task.CompletedTask;
        }
    }
}
