using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Models;
using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Shared.Models;
using BotManager.Backend.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BotManager.Backend.Bots.Services.Implementations
{
    public class BotManagementService
    {
        private readonly BotManagerDbContext _db;
        private readonly ILogger<BotManagementService> _logger;
        private readonly IDiscordBotService _discordBotService;
        private readonly IGroupDataService _groupDataService;
        private readonly IBotDataService _botDataService;
        private readonly CommandManagementService _commandManagementService;
        private readonly IDiscordCommandProvider _discordCommandProvider;

        public BotManagementService(
            BotManagerDbContext db,
            ILogger<BotManagementService> logger,
            IDiscordBotService discordBotService,
            IGroupDataService groupDataService,
            IBotDataService botDataService,
            CommandManagementService commandManagementService,
            IDiscordCommandProvider discordCommandProvider)
        {
            _db = db;
            _logger = logger;
            _discordBotService = discordBotService;
            _groupDataService = groupDataService;
            _botDataService = botDataService;
            _commandManagementService = commandManagementService;
            _discordCommandProvider = discordCommandProvider;
        }

        public async Task<bool> StartBotAsync(int botId)
        {
            var bot = await _db.Bots.FindAsync(botId);
            if (bot == null) return false;

            try
            {
                var openHistory = await _db.BotHistories
                    .Where(h => h.BotId == botId && h.StoppedAt == null)
                    .FirstOrDefaultAsync();
                if (openHistory != null)
                {
                    openHistory.StoppedAt = DateTime.UtcNow;
                    openHistory.DurationSeconds = (long)(openHistory.StoppedAt.Value - openHistory.StartedAt).TotalSeconds;
                    openHistory.StopReason = "Přepsáno novým spuštěním";
                }

                _db.BotHistories.Add(new BotHistory { BotId = botId, StartedAt = DateTime.UtcNow });

                bot.Status = BotStatus.Online;
                bot.LastStartedAt = DateTime.UtcNow;
                bot.LastStoppedAt = null;

                await _db.SaveChangesAsync();

                await _discordBotService.StartAsync(bot.BotId, bot.BotToken);
                await EnsureDefaultCommandsRegisteredAsync(bot.BotId);

                _logger.LogInformation("Bot {BotId} ({Name}) started successfully", bot.BotId, bot.Name);
                return true;
            }
            catch (Exception ex)
            {
                bot.Status = BotStatus.Offline;
                await _db.SaveChangesAsync();
                _logger.LogError(ex, "Failed to start bot {BotId} ({Name})", bot.BotId, bot.Name);
                return false;
            }
        }

        public async Task<bool> StopBotAsync(int botId, string reason = "Ruční vypnutí", string? errorDetails = null)
        {
            var bot = await _db.Bots.FindAsync(botId);
            if (bot == null) return false;

            try
            {
                var openHistory = await _db.BotHistories
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

                _logger.LogInformation("Bot {BotId} ({Name}) stopped. Reason: {Reason}", bot.BotId, bot.Name, reason);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping bot {BotId} ({Name})", bot.BotId, bot.Name);
                return false;
            }
        }

        public async Task<bool> RestartBotAsync(int botId)
        {
            await StopBotAsync(botId, "Restart");
            await Task.Delay(2000);
            return await StartBotAsync(botId);
        }

        public async Task<BotGroupsDto> GetBotGroupsAsync(int botId)
        {
            var bot = await _db.Bots.FindAsync(botId);
            if (bot == null) throw new KeyNotFoundException($"Bot {botId} not found");

            return await _groupDataService.GetAsync(botId);
        }

        public async Task<bool> SaveBotGroupsAsync(int botId, BotGroupsDto data)
        {
            try
            {
                await _groupDataService.SaveAsync(botId, data);

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

                var refreshed = await _discordBotService.RefreshBoardMessageAsync(botId, BoardMessageFactory.FromGroups(boardGroups));
                if (!refreshed)
                {
                    _logger.LogInformation("Board refresh skipped or failed for bot {BotId} after group save", botId);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving groups for bot {BotId}", botId);
                return false;
            }
        }

        public async Task<BotTeamsDto> GetBotTeamsAsync(int botId)
        {
            var groups = await GetBotGroupsAsync(botId);
            return GroupContractMapper.ToTeams(groups);
        }

        public async Task<bool> SaveBotTeamsAsync(int botId, BotTeamsDto data)
        {
            return await SaveBotGroupsAsync(botId, GroupContractMapper.FromTeams(data));
        }

        public async Task<BotConfigurationDto> GetBotDataAsync(int botId)
        {
            var bot = await _db.Bots.FindAsync(botId);
            if (bot == null) throw new KeyNotFoundException($"Bot {botId} not found");

            return await _botDataService.GetAsync(botId);
        }

        public async Task<bool> SaveBotDataAsync(int botId, BotConfigurationDto data)
        {
            var bot = await _db.Bots.FindAsync(botId);
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

        private async Task EnsureDefaultCommandsRegisteredAsync(int botId)
        {
            var defaultCommands = _discordCommandProvider.GetCommandRegistrations();
            foreach (var command in defaultCommands)
            {
                await _commandManagementService.RegisterCommandAsync(
                    botId,
                    command.Name,
                    command.Description,
                    minPermissionLevel: command.MinPermissionLevel,
                    userHint: command.UserHint,
                    successMessage: command.SuccessMessage,
                    permissionMessage: command.PermissionMessage,
                    errorMessage: command.ErrorMessage);
            }
        }
    }
}
