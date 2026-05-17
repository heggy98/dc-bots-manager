using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Bots.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Service for managing bot commands registration and usage tracking.
    /// </summary>
    public class CommandManagementService
    {
        private readonly BotManagerDbContext _context;
        private readonly ILogger<CommandManagementService> _logger;
        private readonly IBotNotificationService _notificationService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly BotStatsDebouncer _statsDebouncer;
        private static readonly TimeSpan StatsDebounceWindow = TimeSpan.FromSeconds(2);

        public CommandManagementService(BotManagerDbContext context, ILogger<CommandManagementService> logger, IBotNotificationService notificationService, IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _logger = logger;
            _notificationService = notificationService;
            _scopeFactory = scopeFactory;
            _statsDebouncer = new BotStatsDebouncer(StatsDebounceWindow, PushStatsSnapshotAsync);
        }

        /// <summary>
        /// Register or update a command in the database.
        /// </summary>
        public async Task RegisterCommandAsync(int botId, string commandName, string? subCommandName, string description,
            int minPermissionLevel = 0, string? userHint = null, string? successMessage = null,
            string? permissionMessage = null, string? errorMessage = null)
        {
            try
            {
                var existingCommand = await _context.BotCommands
                    .FirstOrDefaultAsync(c => c.BotId == botId && c.CommandName == commandName && c.SubCommandName == subCommandName);

                if (existingCommand != null)
                {
                    // Update existing command
                    existingCommand.Description = description;
                    existingCommand.MinimumPermissionLevel = minPermissionLevel;
                    existingCommand.UserHint = userHint;
                    existingCommand.SuccessMessage = successMessage;
                    existingCommand.PermissionMessage = permissionMessage;
                    existingCommand.ErrorMessage = errorMessage;
                    existingCommand.UpdatedAt = DateTime.UtcNow;
                    _context.BotCommands.Update(existingCommand);
                }
                else
                {
                    // Create new command
                    var command = new BotCommand
                    {
                        BotId = botId,
                        CommandName = commandName,
                        SubCommandName = subCommandName,
                        Description = description,
                        MinimumPermissionLevel = minPermissionLevel,
                        UserHint = userHint,
                        SuccessMessage = successMessage,
                        PermissionMessage = permissionMessage,
                        ErrorMessage = errorMessage,
                        IsEnabled = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.BotCommands.Add(command);
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Registered command {CommandName}/{SubCommandName} for bot {BotId}", commandName, subCommandName, botId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering command {CommandName}/{SubCommandName} for bot {BotId}", commandName, subCommandName, botId);
                throw;
            }
        }

        /// <summary>
        /// Get a command by name and bot ID.
        /// </summary>
        public async Task<BotCommand?> GetCommandAsync(int botId, string commandName)
        {
            var normalized = commandName.Trim();

            var existing = await _context.BotCommands
                .Where(c => c.BotId == botId)
                .OrderByDescending(c => c.SubCommandName == normalized)
                .ThenByDescending(c => c.CommandName == normalized)
                .FirstOrDefaultAsync(c =>
                    c.SubCommandName == normalized ||
                    (c.SubCommandName == null && c.CommandName == normalized) ||
                    c.CommandName == normalized);

            if (existing != null)
            {
                return existing;
            }

            // Auto-register missing command metadata so usage logs always have a valid FK target.
            // Reaction events use standalone names (reaction-assign, reaction-unassign) — use them as CommandName directly.
            var isStandaloneEvent = normalized.StartsWith("reaction-", StringComparison.OrdinalIgnoreCase);
            var created = new BotCommand
            {
                BotId = botId,
                CommandName = isStandaloneEvent ? normalized : "board",
                SubCommandName = isStandaloneEvent ? null : normalized,
                Description = $"Auto-registered: {normalized}",
                MinimumPermissionLevel = 0,
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                _context.BotCommands.Add(created);
                await _context.SaveChangesAsync();
                _logger.LogWarning("Auto-registered missing command metadata for bot {BotId}: {SubCommand}", botId, normalized);
                return created;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Race while auto-registering command {SubCommand} for bot {BotId}. Retrying lookup.", normalized, botId);
                return await _context.BotCommands
                    .FirstOrDefaultAsync(c => c.BotId == botId && (c.SubCommandName == normalized || c.CommandName == normalized));
            }
        }

        /// <summary>
        /// Get all commands for a bot.
        /// </summary>
        public async Task<List<BotCommand>> GetCommandsForBotAsync(int botId)
        {
            return await _context.BotCommands
                .Where(c => c.BotId == botId && c.IsEnabled)
                .OrderBy(c => c.CommandName)
                .ToListAsync();
        }

        /// <summary>
        /// Log a command execution.
        /// </summary>
        public async Task LogCommandUsageAsync(int commandId, ulong userId, string? userName = null, bool isSuccess = true, string? errorMessage = null)
        {
            try
            {
                var log = new CommandUsageLog
                {
                    CommandId = commandId,
                    UserId = userId,
                    UserName = userName,
                    ExecutedAt = DateTime.UtcNow,
                    IsSuccess = isSuccess,
                    ErrorMessage = errorMessage
                };

                _context.CommandUsageLogs.Add(log);
                await _context.SaveChangesAsync();

                // Push real-time notifications to connected clients.
                await PushLogNotificationsAsync(log.CommandId, userName ?? userId.ToString(), isSuccess, errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging command usage for command {CommandId}", commandId);
                // Don't throw - usage logging shouldn't break the application
            }
        }

        /// <summary>
        /// Fires real-time SignalR notifications for a freshly inserted command-usage log.
        /// Runs as a background task so it never blocks the hot path.
        /// </summary>
        private async Task PushLogNotificationsAsync(int commandId, string userLabel, bool isSuccess, string? errorMessage)
        {
            try
            {
                var cmd = await _context.BotCommands
                    .AsNoTracking()
                    .Where(c => c.CommandId == commandId)
                    .Select(c => new { c.BotId, c.CommandName, c.SubCommandName })
                    .FirstOrDefaultAsync();

                if (cmd == null) return;

                var label = cmd.CommandName.StartsWith("reaction-", StringComparison.OrdinalIgnoreCase)
                    ? $"[{cmd.CommandName}]"
                    : $"/{cmd.CommandName}{(string.IsNullOrWhiteSpace(cmd.SubCommandName) ? "" : $" {cmd.SubCommandName}")}";

                var level = isSuccess ? "Information" : "Warning";
                var message = isSuccess
                    ? $"{label} by {userLabel} - OK"
                    : $"{label} by {userLabel} - FAILED: {errorMessage ?? "Unknown error"}";

                await _notificationService.NotifyNewLogAsync(cmd.BotId, level, message, DateTime.UtcNow);
                _statsDebouncer.Schedule(cmd.BotId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push SignalR log notifications for command {CommandId}", commandId);
            }
        }

        /// <summary>
        /// Computes and pushes a consolidated 24h requests/errors snapshot for a bot.
        /// </summary>
        private async Task PushStatsSnapshotAsync(int botId, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();
                var cutoff = DateTime.UtcNow.AddHours(-24);

                var aggregated = await (
                    from log in db.CommandUsageLogs
                    join cmd in db.BotCommands on log.CommandId equals cmd.CommandId
                    where cmd.BotId == botId && log.ExecutedAt >= cutoff
                    group log by 1 into g
                    select new
                    {
                        Requests = g.Count(),
                        Errors = g.Count(x => !x.IsSuccess)
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                var requests = aggregated?.Requests ?? 0;
                var errors = aggregated?.Errors ?? 0;

                await _notificationService.NotifyStatsUpdatedAsync(botId, requests, errors);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer command log event for the same bot.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push debounced stats update for bot {BotId}", botId);
            }
        }

        /// <summary>
        /// Get usage statistics for a command.
        /// </summary>
        public async Task<CommandUsageStats> GetCommandUsageStatsAsync(int commandId, int days = 30)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-days);

            var logs = await _context.CommandUsageLogs
                .Where(l => l.CommandId == commandId && l.ExecutedAt >= cutoffDate)
                .ToListAsync();

            var totalUses = logs.Count;
            var successfulUses = logs.Count(l => l.IsSuccess);
            var failedUses = logs.Count(l => !l.IsSuccess);
            var uniqueUsers = logs.Select(l => l.UserId).Distinct().Count();

            var topUsers = logs
                .GroupBy(l => new { l.UserId, l.UserName })
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new UserUsageInfo { UserId = g.Key.UserId, UserName = g.Key.UserName, UseCount = g.Count() })
                .ToList();

            return new CommandUsageStats
            {
                TotalUses = totalUses,
                SuccessfulUses = successfulUses,
                FailedUses = failedUses,
                UniqueUsers = uniqueUsers,
                TopUsers = topUsers
            };
        }
    }

    /// <summary>
    /// Aggregated usage statistics for a command over a selected time window.
    /// </summary>
    public class CommandUsageStats
    {
        public int TotalUses { get; set; }
        public int SuccessfulUses { get; set; }
        public int FailedUses { get; set; }
        public int UniqueUsers { get; set; }
        public List<UserUsageInfo> TopUsers { get; set; } = new();
    }

    /// <summary>
    /// Usage count information for a single user.
    /// </summary>
    public class UserUsageInfo
    {
        public ulong UserId { get; set; }
        public string? UserName { get; set; }
        public int UseCount { get; set; }
    }
}
