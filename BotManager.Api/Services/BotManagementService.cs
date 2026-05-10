using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using BotManager.Api.Models;

namespace BotManager.Api.Services
{
    public class BotManagementService
    {
        private readonly BotManagerDbContext _db;
        private readonly ILogger<BotManagementService> _logger;
        private readonly IDiscordBotService _discordBotService;
        private readonly ITeamsDataService _teamsDataService;
        private readonly IBotDataService _botDataService;
        private readonly CommandManagementService _commandManagementService;

        public BotManagementService(
            BotManagerDbContext db, 
            ILogger<BotManagementService> logger,
            IDiscordBotService discordBotService,
            ITeamsDataService teamsDataService,
            IBotDataService botDataService,
            CommandManagementService commandManagementService)
        {
            _db = db;
            _logger = logger;
            _discordBotService = discordBotService;
            _teamsDataService = teamsDataService;
            _botDataService = botDataService;
            _commandManagementService = commandManagementService;
        }

        public async Task<bool> StartBotAsync(int botId)
        {
            var bot = await _db.Bots.FindAsync(botId);
            if (bot == null) return false;

            try
            {
                // Close any open history record
                var openHistory = await _db.BotHistories
                    .Where(h => h.BotId == botId && h.StoppedAt == null)
                    .FirstOrDefaultAsync();
                if (openHistory != null)
                {
                    openHistory.StoppedAt = DateTime.UtcNow;
                    openHistory.DurationSeconds = (long)(openHistory.StoppedAt.Value - openHistory.StartedAt).TotalSeconds;
                    openHistory.StopReason = "Přepsáno novým spuštěním";
                }

                // Create new history record
                _db.BotHistories.Add(new BotHistory { BotId = botId, StartedAt = DateTime.UtcNow });

                bot.Status = BotStatus.Online;
                bot.LastStartedAt = DateTime.UtcNow;
                bot.LastStoppedAt = null;

                await _db.SaveChangesAsync();

                // Start the actual bot service
                await _discordBotService.StartAsync(bot.BotId, bot.BotToken);

                // Ensure command metadata exists for usage/error tracking.
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
                // Close open history record
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

                // Stop the actual bot service
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
            await Task.Delay(2000); // Brief delay between stop and start
            return await StartBotAsync(botId);
        }

        public async Task<BotTeamsDto> GetBotTeamsAsync(int botId)
        {
            var bot = await _db.Bots.FindAsync(botId);
            if (bot == null) throw new KeyNotFoundException($"Bot {botId} not found");

            return await _teamsDataService.GetAsync(botId);
        }

        public async Task<bool> SaveBotTeamsAsync(int botId, BotTeamsDto data)
        {
            try
            {
                await _teamsDataService.SaveAsync(botId, data);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving teams for bot {BotId}", botId);
                return false;
            }
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
            await _commandManagementService.RegisterCommandAsync(
                botId,
                "pridat-tym",
                "Přidá nový tým do seznamu.",
                minPermissionLevel: 0,
                userHint: "Zadej název týmu, jméno velitele a kontakt.",
                successMessage: "✓ Tým '{0}' byl úspěšně přidán!",
                permissionMessage: "Pro spuštění tohoto příkazu nemáš oprávnění.",
                errorMessage: "Chyba: {0}");

            await _commandManagementService.RegisterCommandAsync(
                botId,
                "odebrat-tym",
                "Odebere tým ze seznamu (včetně role).",
                minPermissionLevel: 0,
                userHint: "Zadej název týmu ke smazání.",
                successMessage: "✓ Tým '{0}' byl úspěšně odstraněn!",
                permissionMessage: "Pro spuštění tohoto příkazu nemáš oprávnění.",
                errorMessage: "Chyba: {0}");

            await _commandManagementService.RegisterCommandAsync(
                botId,
                "upravit-tym",
                "Upraví data týmu.",
                minPermissionLevel: 0,
                userHint: "Zadej název týmu a nová data.",
                successMessage: "✓ Tým '{0}' byl upraven!",
                permissionMessage: "Pro spuštění tohoto příkazu nemáš oprávnění.",
                errorMessage: "Chyba: {0}");

            await _commandManagementService.RegisterCommandAsync(
                botId,
                "seznam-tymu",
                "Zobrazí seznam všech týmů.",
                minPermissionLevel: 1,
                userHint: "Použij /seznam-tymu.",
                successMessage: "✓ Seznam týmů byl aktualizován!",
                permissionMessage: "Tento příkaz je pouze pro administrátory!",
                errorMessage: "Chyba: {0}");

            await _commandManagementService.RegisterCommandAsync(
                botId,
                "reorganizeemojis",
                "Reorganizuje emoji týmů.",
                minPermissionLevel: 1,
                userHint: "Použij /reorganizeemojis.",
                successMessage: "✓ Emojis byly úspěšně reorganizovány!",
                permissionMessage: "Tento příkaz je pouze pro administrátory!",
                errorMessage: "Chyba: {0}");

            await _commandManagementService.RegisterCommandAsync(
                botId,
                "presun-reakce",
                "Přesune zprávu pro reakční role.",
                minPermissionLevel: 1,
                userHint: "Zadej cílový kanál pro zprávu.",
                successMessage: "✓ Zpráva byla úspěšně přesunuta!",
                permissionMessage: "Tento příkaz je pouze pro administrátory!",
                errorMessage: "Chyba: {0}");
        }
    }
}
