using BotManager.Api.Models;
using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Contracts.Models;
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
        private readonly DiscordBotIdentityService _discordBotIdentityService;
        private readonly ILogger<BotController> _logger;

        public BotController(
            BotManagerDbContext db,
            BotManagementService botService,
            DiscordBotIdentityService discordBotIdentityService,
            ILogger<BotController> logger)
        {
            _db = db;
            _botService = botService;
            _discordBotIdentityService = discordBotIdentityService;
            _logger = logger;
        }

        [HttpGet("public")]
        public async Task<IActionResult> GetPublicBots()
        {
            var bots = await _db.Bots.ToListAsync();

            var dtoTasks = bots.Select(async b =>
            {
                var identityTask = _discordBotIdentityService.GetIdentityAsync(b.BotToken);
                var guildsTask = _discordBotIdentityService.GetGuildNamesAsync(b.BotToken);
                await Task.WhenAll(identityTask, guildsTask);
                var identity = identityTask.Result;
                var guilds = guildsTask.Result;
                return new BotPublicDto
                {
                    BotId = b.BotId,
                    Name = b.Name,
                    DiscordBotName = identity?.Name,
                    DiscordBotAvatarUrl = identity?.AvatarUrl,
                    ServerCount = guilds.Count > 0 ? guilds.Count : null,
                    Status = b.Status.ToString(),
                    LastStartedAt = b.LastStartedAt,
                    LastStoppedAt = b.LastStoppedAt
                };
            });

            var dto = await Task.WhenAll(dtoTasks);
            return Ok(dto);
        }

        [Authorize]
        [HttpGet("admin")]
        public async Task<IActionResult> GetAdminBots()
        {
            var bots = await _db.Bots.Include(b => b.Histories).ToListAsync();

            var dtoTasks = bots.Select(async b =>
            {
                var identityTask = _discordBotIdentityService.GetIdentityAsync(b.BotToken);
                var guildsTask = _discordBotIdentityService.GetGuildNamesAsync(b.BotToken);
                await Task.WhenAll(identityTask, guildsTask);
                var identity = identityTask.Result;
                var guilds = guildsTask.Result;
                return new AdminBotDto
                {
                    BotId = b.BotId,
                    Name = b.Name,
                    BotToken = b.BotToken,
                    DiscordBotName = identity?.Name,
                    DiscordBotAvatarUrl = identity?.AvatarUrl,
                    ServerCount = guilds.Count > 0 ? guilds.Count : null,
                    Status = b.Status.ToString(),
                    LastStartedAt = b.LastStartedAt,
                    LastStoppedAt = b.LastStoppedAt,
                    Requests24h = 0,
                    Errors24h = 0
                };
            });

            var dto = await Task.WhenAll(dtoTasks);
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
            var systemLogs = await _db.SystemLogs
                .Where(l => l.Category.Contains("Bot") || l.Message.Contains(bot.Name))
                .OrderByDescending(l => l.Timestamp)
                .Take(50)
                .Select(l => new BotLogDto { Timestamp = l.Timestamp, Level = l.Level, Message = l.Message })
                .ToListAsync();

            var commandLogs = await _db.CommandUsageLogs
                .Where(l => l.BotCommand != null && l.BotCommand.BotId == bot.BotId)
                .OrderByDescending(l => l.ExecutedAt)
                .Take(50)
                .Select(l => new BotLogDto
                {
                    Timestamp = l.ExecutedAt,
                    Level = l.IsSuccess ? "Information" : "Warning",
                    Message = l.IsSuccess
                        ? $"/{l.BotCommand!.CommandName} by {l.UserName ?? l.UserId.ToString()} - OK"
                        : $"/{l.BotCommand!.CommandName} by {l.UserName ?? l.UserId.ToString()} - FAILED: {l.ErrorMessage ?? "Unknown error"}"
                })
                .ToListAsync();

            var logs = systemLogs
                .Concat(commandLogs)
                .OrderByDescending(l => l.Timestamp)
                .Take(80)
                .ToList();

            var botConfig = await _botService.GetBotDataAsync(id);
            var identityTask = _discordBotIdentityService.GetIdentityAsync(bot.BotToken);
            var guildsTask = _discordBotIdentityService.GetGuildNamesAsync(bot.BotToken);
            await Task.WhenAll(identityTask, guildsTask);
            var identity = identityTask.Result;
            var guilds = guildsTask.Result;

            var dto = new AdminBotDetailDto
            {
                BotId = bot.BotId,
                Name = bot.Name,
                BotToken = bot.BotToken,
                DiscordBotName = identity?.Name,
                DiscordBotAvatarUrl = identity?.AvatarUrl,
                ServerCount = guilds.Count > 0 ? guilds.Count : null,
                Guilds = guilds,
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
        [HttpGet("admin/{id}/groups")]
        public async Task<IActionResult> GetGroups(int id)
        {
            try
            {
                var groupsData = await _botService.GetBotGroupsAsync(id);
                return Ok(groupsData);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching groups for bot {Id}", id);
                return StatusCode(500, new { message = "Error fetching groups" });
            }
        }

        [Authorize]
        [HttpPost("admin/{id}/groups")]
        public async Task<IActionResult> SaveGroups(int id, [FromBody] BotGroupsDto groupsData)
        {
            try
            {
                var success = await _botService.SaveBotGroupsAsync(id, groupsData);
                if (!success) return StatusCode(500, new { message = "Error saving groups" });
                return Ok(new { message = "Groups saved successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving groups for bot {Id}", id);
                return StatusCode(500, new { message = "Error saving groups" });
            }
        }

        [Authorize]
        [HttpGet("admin/{id}/teams")]
        public async Task<IActionResult> GetTeams(int id)
        {
            try
            {
                var teamsData = GroupContractMapper.ToTeams(await _botService.GetBotGroupsAsync(id));
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
                var success = await _botService.SaveBotGroupsAsync(id, GroupContractMapper.FromTeams(teamsData));
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
