using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Starts bots flagged with <c>AutoStart</c> once the application has started.
    /// Bots are started one after another so a slow Discord handshake does not stall the others' logs.
    /// Can be disabled with <c>Bots:AutoStartEnabled=false</c>.
    /// </summary>
    public class BotAutoStartService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BotAutoStartService> _logger;

        /// <summary>
        /// Creates a new auto-start service.
        /// </summary>
        public BotAutoStartService(IServiceScopeFactory scopeFactory, IHostApplicationLifetime lifetime,
            IConfiguration configuration, ILogger<BotAutoStartService> logger)
        {
            _scopeFactory = scopeFactory;
            _lifetime = lifetime;
            _configuration = configuration;
            _logger = logger;
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (string.Equals(_configuration["Bots:AutoStartEnabled"], "false", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Wait until the host is fully started (migrations done, endpoints listening).
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (_lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
            using (stoppingToken.Register(() => started.TrySetCanceled(stoppingToken)))
            {
                try
                {
                    await started.Task;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            List<int> botIds;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();
                botIds = await db.Bots
                    .AsNoTracking()
                    .Where(b => b.AutoStart)
                    .OrderBy(b => b.BotId)
                    .Select(b => b.BotId)
                    .ToListAsync(stoppingToken);
            }

            foreach (var botId in botIds)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var botService = scope.ServiceProvider.GetRequiredService<BotManagementService>();
                    var ok = await botService.StartBotAsync(botId);
                    _logger.LogInformation("Auto-start of bot {BotId} {Result}", botId, ok ? "succeeded" : "failed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Auto-start of bot {BotId} failed", botId);
                }
            }
        }
    }
}
