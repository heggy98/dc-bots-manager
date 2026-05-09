namespace BotManager.Backend.Services.Implementation
{
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using System.Threading;
    using System.Threading.Tasks;

    public class BotStartupService : IHostedService
    {
        private readonly BotManagerService _botManager;
        private readonly ILogger<BotStartupService> _logger;

        public BotStartupService(BotManagerService botManager, ILogger<BotStartupService> logger)
        {
            _botManager = botManager;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Spouštění BotManager při startu aplikace.");
            _botManager.StartBot();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Zastavování BotManager při ukončení aplikace.");
            _botManager.StopBot();
            return Task.CompletedTask;
        }
    }
}
