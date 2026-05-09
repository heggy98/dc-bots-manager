using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Api.Services
{
    /// <summary>
    /// Service for managing bot commands registration and usage tracking.
    /// </summary>
    public class CommandManagementService
    {
        private readonly BotManagerDbContext _context;
        private readonly ILogger<CommandManagementService> _logger;

        public CommandManagementService(BotManagerDbContext context, ILogger<CommandManagementService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Register or update a command in the database.
        /// </summary>
        public async Task RegisterCommandAsync(int botId, string commandName, string description,
            int minPermissionLevel = 0, string? userHint = null, string? successMessage = null,
            string? permissionMessage = null, string? errorMessage = null)
        {
            try
            {
                var existingCommand = await _context.BotCommands
                    .FirstOrDefaultAsync(c => c.BotId == botId && c.CommandName == commandName);

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
                _logger.LogInformation("Registered command {CommandName} for bot {BotId}", commandName, botId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering command {CommandName} for bot {BotId}", commandName, botId);
                throw;
            }
        }

        /// <summary>
        /// Get a command by name and bot ID.
        /// </summary>
        public async Task<BotCommand?> GetCommandAsync(int botId, string commandName)
        {
            return await _context.BotCommands
                .FirstOrDefaultAsync(c => c.BotId == botId && c.CommandName == commandName);
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging command usage for command {CommandId}", commandId);
                // Don't throw - usage logging shouldn't break the application
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

    public class CommandUsageStats
    {
        public int TotalUses { get; set; }
        public int SuccessfulUses { get; set; }
        public int FailedUses { get; set; }
        public int UniqueUsers { get; set; }
        public List<UserUsageInfo> TopUsers { get; set; } = new();
    }

    public class UserUsageInfo
    {
        public ulong UserId { get; set; }
        public string? UserName { get; set; }
        public int UseCount { get; set; }
    }
}
