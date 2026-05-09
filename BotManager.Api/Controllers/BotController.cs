using BotManager.Api.Models;
using BotManager.Api.Services;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BotController : ControllerBase
    {
        private readonly BotManagerDbContext _db;
        private readonly BotManagementService _botService;
        private readonly ILogger<BotController> _logger;

        public BotController(BotManagerDbContext db, BotManagementService botService, ILogger<BotController> logger)
        {
            _db = db;
            _botService = botService;
            _logger = logger;
        }

        [HttpGet("public")]
        public async Task<IActionResult> GetPublicBots()
        {
            var bots = await _db.Bots.ToListAsync();
            var dto = bots.Select(b => new BotPublicDto
            {
                BotId = b.BotId,
                Name = b.Name,
                Status = b.Status.ToString()
            });
            return Ok(dto);
        }

        [Authorize]
        [HttpGet("admin")]
        public async Task<IActionResult> GetAdminBots()
        {
            var bots = await _db.Bots.Include(b => b.Histories).ToListAsync();
            var dto = bots.Select(b => new AdminBotDto
            {
                BotId = b.BotId,
                Name = b.Name,
                BotToken = b.BotToken,
                Status = b.Status.ToString(),
                LastStartedAt = b.LastStartedAt,
                LastStoppedAt = b.LastStoppedAt,
                Requests24h = 0,
                Errors24h = 0
            });
            return Ok(dto);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> CreateBot([FromBody] CreateBotDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.BotToken))
                return BadRequest("Name and Token are required.");

            var newBot = new Bot { Name = request.Name, BotToken = request.BotToken };
            await _db.Bots.AddAsync(newBot);
            await _db.SaveChangesAsync();

            var config = new BotConfiguration { BotId = newBot.BotId };
            await _db.BotConfigurations.AddAsync(config);
            await _db.SaveChangesAsync();

            _logger.LogInformation("New bot created: {Name} (ID {Id})", newBot.Name, newBot.BotId);
            return Ok(newBot.BotId);
        }

        [Authorize]
        [HttpGet("admin/{id}")]
        public async Task<IActionResult> GetBotDetail(int id)
        {
            var bot = await _db.Bots
                .Include(b => b.Histories.OrderByDescending(h => h.StartedAt).Take(50))
                .FirstOrDefaultAsync(b => b.BotId == id);

            if (bot == null) return NotFound();

            // Get real system logs for this bot
            var logs = await _db.SystemLogs
                .Where(l => l.Category.Contains("Bot") || l.Message.Contains(bot.Name))
                .OrderByDescending(l => l.Timestamp)
                .Take(50)
                .Select(l => new BotLogDto { Timestamp = l.Timestamp, Level = l.Level, Message = l.Message })
                .ToListAsync();

            var botConfig = await _botService.GetBotDataAsync(id);

            var dto = new AdminBotDetailDto
            {
                BotId = bot.BotId,
                Name = bot.Name,
                BotToken = bot.BotToken,
                Status = bot.Status.ToString(),
                LastStartedAt = bot.LastStartedAt,
                LastStoppedAt = bot.LastStoppedAt,
                Requests24h = 0,
                Errors24h = 0,
                Configuration = botConfig,
                Logs = logs,
                Histories = bot.Histories.Select(h => new BotHistoryDto
                {
                    Id = h.Id,
                    StartedAt = h.StartedAt,
                    StoppedAt = h.StoppedAt,
                    DurationSeconds = h.DurationSeconds,
                    StopReason = h.StopReason,
                    ErrorDetails = h.ErrorDetails
                }).ToList()
            };

            return Ok(dto);
        }

        [Authorize]
        [HttpPut("admin/{id}/config")]
        public async Task<IActionResult> UpdateBotConfiguration(int id, [FromBody] BotConfigurationDto request)
        {
            var success = await _botService.SaveBotDataAsync(id, request);
            if (!success) return NotFound();
            _logger.LogInformation("Bot {Id} configuration updated", id);
            return Ok();
        }

        [Authorize]
        [HttpPost("admin/{id}/start")]
        public async Task<IActionResult> StartBot(int id)
        {
            var success = await _botService.StartBotAsync(id);
            if (!success) return NotFound();
            return Ok(new { message = $"Bot {id} started." });
        }

        [Authorize]
        [HttpPost("admin/{id}/stop")]
        public async Task<IActionResult> StopBot(int id)
        {
            var success = await _botService.StopBotAsync(id, "Ruční vypnutí");
            if (!success) return NotFound();
            return Ok(new { message = $"Bot {id} stopped." });
        }

        [Authorize]
        [HttpPost("admin/{id}/restart")]
        public async Task<IActionResult> RestartBot(int id)
        {
            var success = await _botService.RestartBotAsync(id);
            if (!success) return NotFound();
            return Ok(new { message = $"Bot {id} restarted." });
        }

        [Authorize]
        [HttpGet("admin/{id}/teams")]
        public async Task<IActionResult> GetTeams(int id)
        {
            try
            {
                var teamsData = await _botService.GetBotTeamsAsync(id);
                return Ok(teamsData);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching teams for bot {Id}", id);
                return StatusCode(500, new { message = "Error fetching teams" });
            }
        }

        [Authorize]
        [HttpPost("admin/{id}/teams")]
        public async Task<IActionResult> SaveTeams(int id, [FromBody] BotTeamsDto teamsData)
        {
            try
            {
                var success = await _botService.SaveBotTeamsAsync(id, teamsData);
                if (!success) return StatusCode(500, new { message = "Error saving teams" });
                return Ok(new { message = "Teams saved successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving teams for bot {Id}", id);
                return StatusCode(500, new { message = "Error saving teams" });
            }
        }
    }
}
