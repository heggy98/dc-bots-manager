using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Models;
using BotManager.Backend.Shared.Models;
using BotManager.Backend.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Coordinates bot lifecycle operations and bot-scoped data persistence.
    /// </summary>
    public class BotManagementService
    {
        private const int RestartDelayMilliseconds = 2000;

        private readonly BotManagerDbContext _db;
        private readonly ILogger<BotManagementService> _logger;
        private readonly IDiscordBotService _discordBotService;
        private readonly IGroupDataService _groupDataService;
        private readonly IBotDataService _botDataService;
        private readonly CommandManagementService _commandManagementService;
        private readonly IDiscordCommandProvider _discordCommandProvider;
        private readonly IBotNotificationService _notificationService;
        private readonly IBotTokenSecurityService _botTokenSecurityService;

        /// <summary>
        /// Creates a new bot management service.
        /// </summary>
        public BotManagementService(
            BotManagerDbContext db,
            ILogger<BotManagementService> logger,
            IDiscordBotService discordBotService,
            IGroupDataService groupDataService,
            IBotDataService botDataService,
            CommandManagementService commandManagementService,
            IDiscordCommandProvider discordCommandProvider,
            IBotNotificationService notificationService,
            IBotTokenSecurityService botTokenSecurityService)
        {
            _db = db;
            _logger = logger;
            _discordBotService = discordBotService;
            _groupDataService = groupDataService;
            _botDataService = botDataService;
            _commandManagementService = commandManagementService;
            _discordCommandProvider = discordCommandProvider;
            _notificationService = notificationService;
            _botTokenSecurityService = botTokenSecurityService;
        }

        /// <summary>
        /// Starts a bot instance and records runtime history.
        /// </summary>
        public async Task<bool> StartBotAsync(int botId)
        {
            var bot = await GetBotOrNullAsync(botId);
            if (bot == null) return false;

            try
            {
                var openHistory = await _db.BotRunHistories
                    .Where(h => h.BotId == botId && h.StoppedAt == null)
                    .FirstOrDefaultAsync();
                if (openHistory != null)
                {
                    openHistory.StoppedAt = DateTime.UtcNow;
                    openHistory.DurationSeconds = (long)(openHistory.StoppedAt.Value - openHistory.StartedAt).TotalSeconds;
                    openHistory.StopReason = "Přepsáno novým spuštěním";
                }

                _db.BotRunHistories.Add(new BotRunHistory { BotId = botId, StartedAt = DateTime.UtcNow });

                bot.Status = BotStatus.Connecting;
                bot.LastStartedAt = DateTime.UtcNow;
                bot.LastStoppedAt = null;

                await _db.SaveChangesAsync();
                await _notificationService.NotifyBotStatusChangedAsync(botId, BotStatus.Connecting);

                if (!_botTokenSecurityService.TryGetRawToken(bot.BotToken, out var rawToken))
                {
                    throw new InvalidOperationException("Stored bot token is missing or invalid.");
                }

                await _discordBotService.StartAsync(bot.BotId, rawToken);

                bot.Status = BotStatus.Online;
                await _db.SaveChangesAsync();

                await EnsureDefaultCommandsRegisteredAsync(bot.BotId);

                await _notificationService.NotifyBotStatusChangedAsync(botId, BotStatus.Online);
                await _notificationService.NotifyHistoryUpdatedAsync(botId);

                _logger.LogInformation("Bot {BotId} ({Name}) started successfully", bot.BotId, bot.Name);
                return true;
            }
            catch (Exception ex)
            {
                bot.Status = BotStatus.Offline;
                await _db.SaveChangesAsync();
                await _notificationService.NotifyBotStatusChangedAsync(botId, BotStatus.Offline);
                _logger.LogError(ex, "Failed to start bot {BotId} ({Name})", bot.BotId, bot.Name);
                return false;
            }
        }

        /// <summary>
        /// Stops a bot instance and closes runtime history with a stop reason.
        /// </summary>
        public async Task<bool> StopBotAsync(int botId, string reason = "Ruční vypnutí", string? errorDetails = null)
        {
            var bot = await GetBotOrNullAsync(botId);
            if (bot == null) return false;

            try
            {
                bot.Status = BotStatus.Disconnecting;
                await _db.SaveChangesAsync();
                await _notificationService.NotifyBotStatusChangedAsync(botId, BotStatus.Disconnecting);

                var openHistory = await _db.BotRunHistories
                    .Where(h => h.BotId == botId && h.StoppedAt == null)
                    .FirstOrDefaultAsync();

                if (openHistory != null)
                {
                    openHistory.StoppedAt = DateTime.UtcNow;
                    openHistory.DurationSeconds = (long)(openHistory.StoppedAt.Value - openHistory.StartedAt).TotalSeconds;
                    openHistory.StopReason = reason;
                    openHistory.ErrorDetails = errorDetails;
                }

                bot.Status = BotStatus.Offline;
                bot.LastStoppedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync();
                await _discordBotService.StopAsync();

                await _notificationService.NotifyBotStatusChangedAsync(botId, BotStatus.Offline);
                await _notificationService.NotifyHistoryUpdatedAsync(botId);

                _logger.LogInformation("Bot {BotId} ({Name}) stopped. Reason: {Reason}", bot.BotId, bot.Name, reason);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping bot {BotId} ({Name})", bot.BotId, bot.Name);
                return false;
            }
        }

        /// <summary>
        /// Restarts a bot instance by stopping and then starting it again.
        /// </summary>
        public async Task<bool> RestartBotAsync(int botId)
        {
            await StopBotAsync(botId, "Restart");
            await Task.Delay(RestartDelayMilliseconds);
            return await StartBotAsync(botId);
        }

        /// <summary>
        /// Loads group data for a bot.
        /// </summary>
        public async Task<BotGroupsDto> GetBotGroupsAsync(int botId, int? boardConfigurationId = null)
        {
            await EnsureBotExistsAsync(botId);

            return await _groupDataService.GetAsync(botId, boardConfigurationId);
        }

        /// <summary>
        /// Persists group data and attempts to refresh the board message.
        /// </summary>
        public async Task<bool> SaveBotGroupsAsync(int botId, BotGroupsDto data, int? boardConfigurationId = null)
        {
            try
            {
                var previousGroups = await _groupDataService.GetAsync(botId, boardConfigurationId);
                await _groupDataService.SaveAsync(botId, data, boardConfigurationId);

                var boardGroups = new BoardGroupCollectionDto
                {
                    Groups = data.Groups.Select(group => new BoardGroupDto
                    {
                        Id = group.GroupId,
                        Name = group.Name,
                        Subtitle = group.OwnerName,
                        Contact = group.Contact,
                        Emoji = group.Icon
                    }).ToList()
                };

                var boardConfig = await _botDataService.GetAsync(botId);
                var refreshed = await _discordBotService.RefreshBoardMessageAsync(botId, BoardMessageFactory.FromGroups(boardGroups, boardConfig), boardConfigurationId);
                if (!refreshed)
                {
                    _logger.LogInformation("Board refresh skipped or failed for bot {BotId} after group save", botId);
                }

                var teamCountChanged = previousGroups.Groups.Count != data.Groups.Count;
                if (teamCountChanged)
                {
                    var emojis = data.Groups
                        .Select(group => string.IsNullOrWhiteSpace(group.Icon) ? "🎯" : group.Icon)
                        .ToList();

                    var reactionsSynced = await _discordBotService.SyncBoardReactionsIfPresentAsync(botId, emojis, boardConfigurationId);
                    if (!reactionsSynced)
                    {
                        _logger.LogInformation("Reaction sync skipped or failed for bot {BotId} after team count change", botId);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving groups for bot {BotId}", botId);
                return false;
            }
        }

        /// <summary>
        /// Loads team data for a bot by mapping stored group data.
        /// </summary>
        public async Task<BotTeamsDto> GetBotTeamsAsync(int botId, int? boardConfigurationId = null)
        {
            var groups = await GetBotGroupsAsync(botId, boardConfigurationId);
            return GroupContractMapper.ToTeams(groups);
        }

        /// <summary>
        /// Persists team data by mapping it to group storage DTOs.
        /// </summary>
        public async Task<bool> SaveBotTeamsAsync(int botId, BotTeamsDto data, int? boardConfigurationId = null)
        {
            return await SaveBotGroupsAsync(botId, GroupContractMapper.FromTeams(data), boardConfigurationId);
        }

        /// <summary>
        /// Loads bot configuration data for a bot.
        /// </summary>
        public async Task<BotConfigurationDto> GetBotDataAsync(int botId)
        {
            await EnsureBotExistsAsync(botId);

            return await _botDataService.GetAsync(botId);
        }

        /// <summary>
        /// Saves bot configuration data for a bot.
        /// </summary>
        public async Task<bool> SaveBotDataAsync(int botId, BotConfigurationDto data)
        {
            var bot = await GetBotOrNullAsync(botId);
            if (bot == null) return false;

            try
            {
                await _botDataService.SaveAsync(botId, data);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving bot data for bot {BotId}", botId);
                return false;
            }
        }

        /// <summary>
        /// Registers default command definitions for a bot if missing in storage.
        /// </summary>
        private async Task EnsureDefaultCommandsRegisteredAsync(int botId)
        {
            var defaultCommands = _discordCommandProvider.GetCommandRegistrations();
            foreach (var command in defaultCommands)
            {
                await _commandManagementService.RegisterCommandAsync(
                    botId,
                    command.Name,
                    command.SubCommandName,
                    command.Description,
                    minPermissionLevel: command.MinPermissionLevel,
                    userHint: command.UserHint,
                    successMessage: command.SuccessMessage,
                    permissionMessage: command.PermissionMessage,
                    errorMessage: command.ErrorMessage);
            }
        }

        /// <summary>
        /// Loads a bot by id or returns null when missing.
        /// </summary>
        private async Task<Bot?> GetBotOrNullAsync(int botId)
        {
            return await _db.Bots.FindAsync(botId);
        }

        /// <summary>
        /// Ensures a bot exists for the given id.
        /// </summary>
        private async Task EnsureBotExistsAsync(int botId)
        {
            var bot = await GetBotOrNullAsync(botId);
            if (bot == null)
            {
                throw new KeyNotFoundException($"Bot {botId} not found");
            }
        }
    }
}
