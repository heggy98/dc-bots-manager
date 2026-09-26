using BotManager.Backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Periodically deletes old rows from log and audit tables so they do not grow without bound.
    /// Retention (days) is configurable via LogRetention:SystemLogsDays, :CommandUsageDays and :LoginAuditDays.
    /// </summary>
    public class LogRetentionService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
        private const int BatchSize = 5000;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LogRetentionService> _logger;

        /// <summary>
        /// Creates a new log retention service.
        /// </summary>
        public LogRetentionService(IServiceScopeFactory scopeFactory, IConfiguration configuration,
            ILogger<LogRetentionService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Let startup (migrations) finish first.
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            using var timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    await CleanupAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Log retention cleanup failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task CleanupAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();
            var now = DateTime.UtcNow;

            var systemCutoff = now.AddDays(-GetDays("LogRetention:SystemLogsDays", 30));
            var usageCutoff = now.AddDays(-GetDays("LogRetention:CommandUsageDays", 90));
            var auditCutoff = now.AddDays(-GetDays("LogRetention:LoginAuditDays", 90));

            var removedSystem = await DeleteInBatchesAsync(
                () => db.SystemLogs.Where(l => l.Timestamp < systemCutoff).OrderBy(l => l.Id).Take(BatchSize).ExecuteDeleteAsync(cancellationToken));
            var removedUsage = await DeleteInBatchesAsync(
                () => db.CommandUsageLogs.Where(l => l.ExecutedAt < usageCutoff).OrderBy(l => l.UsageId).Take(BatchSize).ExecuteDeleteAsync(cancellationToken));
            var removedAudit = await DeleteInBatchesAsync(
                () => db.LoginAuditLogs.Where(l => l.Timestamp < auditCutoff).OrderBy(l => l.Id).Take(BatchSize).ExecuteDeleteAsync(cancellationToken));

            // Refresh tokens: drop rows that expired more than a day ago (revoked ones are kept until expiry
            // so that reuse of a rotated token is still detected).
            var tokenCutoff = now.AddDays(-1);
            await db.RefreshTokens.Where(t => t.ExpiresAt < tokenCutoff).ExecuteDeleteAsync(cancellationToken);

            if (removedSystem + removedUsage + removedAudit > 0)
            {
                _logger.LogInformation(
                    "Log retention removed SystemLogs={SystemLogs}, CommandUsageLogs={CommandUsageLogs}, LoginAuditLogs={LoginAuditLogs}",
                    removedSystem, removedUsage, removedAudit);
            }
        }

        private static async Task<int> DeleteInBatchesAsync(Func<Task<int>> deleteBatch)
        {
            var total = 0;
            int removed;
            do
            {
                removed = await deleteBatch();
                total += removed;
            }
            while (removed == BatchSize);

            return total;
        }

        private int GetDays(string key, int defaultValue)
            => int.TryParse(_configuration[key], out var days) && days > 0 ? days : defaultValue;
    }
}
