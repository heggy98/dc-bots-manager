using BotManager.Backend.API.Models;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Shared.Models;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BotManager.Backend.API.Services;

namespace BotManager.Backend.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BotController : ControllerBase
    {
        private readonly BotManagerDbContext _db;
        private readonly BotManagementService _botService;
        private readonly DiscordBotIdentityService _discordBotIdentityService;
        private readonly IUserIdentityResolver _userIdentityResolver;
        private readonly IEmojiCatalogService _emojiCatalogService;
        private readonly IBotTokenSecurityService _botTokenSecurityService;
        private readonly ILogger<BotController> _logger;

        /// <summary>
        /// Creates a new bot management controller.
        /// </summary>
        public BotController(
            BotManagerDbContext db,
            BotManagementService botService,
            DiscordBotIdentityService discordBotIdentityService,
            IUserIdentityResolver userIdentityResolver,
            IEmojiCatalogService emojiCatalogService,
            IBotTokenSecurityService botTokenSecurityService,
            ILogger<BotController> logger)
        {
            _db = db;
            _botService = botService;
            _discordBotIdentityService = discordBotIdentityService;
            _userIdentityResolver = userIdentityResolver;
            _emojiCatalogService = emojiCatalogService;
            _botTokenSecurityService = botTokenSecurityService;
            _logger = logger;
        }

        /// <summary>
        /// Returns a catalog of selectable emojis for team configuration.
        /// </summary>
        [Authorize]
        [HttpGet("admin/emoji-catalog")]
        public async Task<IActionResult> GetEmojiCatalog()
        {
            var emojis = await _emojiCatalogService.GetEmojiCatalogAsync(HttpContext.RequestAborted);
            return Ok(emojis);
        }

        /// <summary>
        /// Returns all publicly visible bots.
        /// </summary>
        [HttpGet("public")]
        public async Task<IActionResult> GetPublicBots()
        {
            var bots = await _db.Bots
                .AsNoTracking()
                .Where(b => b.IsPublic)
                .ToListAsync();

            var dtoTasks = bots.Select(MapPublicBotDtoAsync);

            var dto = await Task.WhenAll(dtoTasks);
            return Ok(dto);
        }

        /// <summary>
        /// Returns bots owned by the currently authenticated user.
        /// </summary>
        [Authorize]
        [HttpGet("mine")]
        public async Task<IActionResult> GetMyBots()
        {
            var ownerUserId = GetCurrentUserIdentifier();
            if (string.IsNullOrWhiteSpace(ownerUserId))
            {
                return Unauthorized("Unable to resolve current user identity.");
            }

            var bots = await _db.Bots
                .AsNoTracking()
                .Where(b => b.OwnerUserId == ownerUserId)
                .ToListAsync();
            var usageStats = await LoadUsageStats24hAsync();

            var dtoTasks = bots.Select(bot => MapAdminBotDtoAsync(bot, usageStats));

            var dto = await Task.WhenAll(dtoTasks);
            return Ok(dto);
        }

        /// <summary>
        /// Returns all bots for administrative overview.
        /// </summary>
        [Authorize]
        [HttpGet("admin")]
        public async Task<IActionResult> GetAdminBots()
        {
            var bots = await _db.Bots
                .AsNoTracking()
                .ToListAsync();
            var usageStats = await LoadUsageStats24hAsync();

            var dtoTasks = bots.Select(bot => MapAdminBotDtoAsync(bot, usageStats));

            var dto = await Task.WhenAll(dtoTasks);
            return Ok(dto);
        }

        /// <summary>
        /// Creates a new bot and initializes its default configuration row.
        /// </summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> CreateBot([FromBody] CreateBotDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.BotToken))
                return BadRequest("Name and Token are required.");

            var normalizedRawToken = _botTokenSecurityService.NormalizeRawToken(request.BotToken);
            if (string.IsNullOrWhiteSpace(normalizedRawToken))
            {
                return BadRequest("Bot token is invalid.");
            }

            var isAuthorized = await IsDiscordTokenAuthorizedAsync(normalizedRawToken);
            if (!isAuthorized)
            {
                return BadRequest("Bot token is not authorized by Discord API.");
            }

            var ownerUserId = GetCurrentUserIdentifier();
            if (string.IsNullOrWhiteSpace(ownerUserId))
            {
                return Unauthorized("Unable to resolve current user identity.");
            }

            var newBot = new Bot
            {
                Name = request.Name,
                BotToken = _botTokenSecurityService.ProtectForStorage(normalizedRawToken),
                OwnerUserId = ownerUserId,
                IsPublic = request.IsPublic
            };
            await _db.Bots.AddAsync(newBot);
            await _db.SaveChangesAsync();

            _logger.LogInformation("New bot created: {Name} (ID {Id})", newBot.Name, newBot.BotId);
            return Ok(newBot.BotId);
        }

        /// <summary>
        /// Returns a detailed administrative view for a specific bot.
        /// </summary>
        [Authorize]
        [HttpGet("admin/{id}")]
        public async Task<IActionResult> GetBotDetail(int id)
        {
            var bot = await _db.Bots
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BotId == id);

            if (bot == null) return NotFound();

            var histories = await _db.BotRunHistories
                .AsNoTracking()
                .Where(h => h.BotId == id)
                .OrderByDescending(h => h.StartedAt)
                .Take(50)
                .ToListAsync();

            var logs = await LoadBotLogsAsync(bot, 80);
            var usageStats = await LoadUsageStats24hAsync();

            var botConfig = await _botService.GetBotDataAsync(id);

            var tokenAuthorized = TryResolveRawToken(bot.BotToken, out var rawToken)
                && await IsDiscordTokenAuthorizedAsync(rawToken!);

            var (identity, guilds) = tokenAuthorized
                ? await LoadDiscordMetadataAsync(rawToken!)
                : (null, []);

            var dto = new AdminBotDetailDto
            {
                BotId = bot.BotId,
                Name = bot.Name,
                BotToken = _botTokenSecurityService.BuildMaskedToken(bot.BotToken),
                OwnerUserId = bot.OwnerUserId,
                IsPublic = bot.IsPublic,
                IsTokenAuthorized = tokenAuthorized,
                DiscordBotName = identity?.Name,
                DiscordBotAvatarUrl = identity?.AvatarUrl,
                ServerCount = guilds.Count > 0 ? guilds.Count : null,
                Guilds = guilds,
                Status = bot.Status.ToString(),
                LastStartedAt = bot.LastStartedAt,
                LastStoppedAt = bot.LastStoppedAt,
                Requests24h = usageStats.TryGetValue(bot.BotId, out var stats) ? stats.Requests24h : 0,
                Errors24h = usageStats.TryGetValue(bot.BotId, out var errorStats) ? errorStats.Errors24h : 0,
                Configuration = botConfig,
                Logs = logs,
                Histories = histories.Select(h => new BotHistoryDto
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

        /// <summary>
        /// Returns merged runtime and command logs for a bot.
        /// </summary>
        [Authorize]
        [HttpGet("admin/{id}/logs")]
        public async Task<IActionResult> GetBotLogs(int id)
        {
            var (bot, errorResult) = await GetBotOrNotFoundAsync(id);
            if (errorResult != null)
            {
                return errorResult;
            }

            var logs = await LoadBotLogsAsync(bot!, 80);
            return Ok(logs);
        }

        /// <summary>
        /// Resolves the current authenticated user identifier from available claims.
        /// </summary>
        private string? GetCurrentUserIdentifier()
        {
            return _userIdentityResolver.GetCurrentUserIdentifier(User);
        }

        /// <summary>
        /// Maps a bot entity to public API DTO including Discord metadata.
        /// </summary>
        private async Task<BotPublicDto> MapPublicBotDtoAsync(Bot bot)
        {
            DiscordBotIdentity? identity = null;
            List<string> guilds = [];

            if (TryResolveRawToken(bot.BotToken, out var rawToken))
            {
                (identity, guilds) = await LoadDiscordMetadataAsync(rawToken!);
            }

            return new BotPublicDto
            {
                BotId = bot.BotId,
                Name = bot.Name,
                OwnerUserId = bot.OwnerUserId,
                IsPublic = bot.IsPublic,
                DiscordBotName = identity?.Name,
                DiscordBotAvatarUrl = identity?.AvatarUrl,
                ServerCount = guilds.Count > 0 ? guilds.Count : null,
                Status = bot.Status.ToString(),
                LastStartedAt = bot.LastStartedAt,
                LastStoppedAt = bot.LastStoppedAt
            };
        }

        /// <summary>
        /// Maps a bot entity to admin API DTO including usage counters and Discord metadata.
        /// </summary>
        private async Task<AdminBotDto> MapAdminBotDtoAsync(Bot bot, Dictionary<int, (int Requests24h, int Errors24h)> usageStats)
        {
            DiscordBotIdentity? identity = null;
            List<string> guilds = [];

            if (TryResolveRawToken(bot.BotToken, out var rawToken))
            {
                (identity, guilds) = await LoadDiscordMetadataAsync(rawToken!);
            }

            return new AdminBotDto
            {
                BotId = bot.BotId,
                Name = bot.Name,
                BotToken = _botTokenSecurityService.BuildMaskedToken(bot.BotToken),
                OwnerUserId = bot.OwnerUserId,
                IsPublic = bot.IsPublic,
                DiscordBotName = identity?.Name,
                DiscordBotAvatarUrl = identity?.AvatarUrl,
                ServerCount = guilds.Count > 0 ? guilds.Count : null,
                Status = bot.Status.ToString(),
                LastStartedAt = bot.LastStartedAt,
                LastStoppedAt = bot.LastStoppedAt,
                Requests24h = usageStats.TryGetValue(bot.BotId, out var stats) ? stats.Requests24h : 0,
                Errors24h = usageStats.TryGetValue(bot.BotId, out var errorStats) ? errorStats.Errors24h : 0
            };
        }

        /// <summary>
        /// Loads Discord identity and guild metadata in parallel for a bot token.
        /// </summary>
        private async Task<(DiscordBotIdentity? Identity, List<string> Guilds)> LoadDiscordMetadataAsync(string botToken)
        {
            var identityTask = _discordBotIdentityService.GetIdentityAsync(botToken);
            var guildsTask = _discordBotIdentityService.GetGuildNamesAsync(botToken);
            await Task.WhenAll(identityTask, guildsTask);
            return (identityTask.Result, guildsTask.Result);
        }

        /// <summary>
        /// Updates persisted bot configuration values.
        /// </summary>
        [Authorize]
        [HttpPut("admin/{id}/config")]
        public async Task<IActionResult> UpdateBotConfiguration(int id, [FromBody] BotConfigurationDto request)
        {
            var success = await _botService.SaveBotDataAsync(id, request);
            if (!success) return NotFound();
            _logger.LogInformation("Bot {Id} configuration updated", id);
            return Ok();
        }

        /// <summary>
        /// Updates bot visibility for public listing.
        /// </summary>
        [Authorize]
        [HttpPut("admin/{id}/visibility")]
        public async Task<IActionResult> UpdateBotVisibility(int id, [FromBody] UpdateBotVisibilityRequest request)
        {
            var (bot, errorResult) = await GetBotOrNotFoundAsync(id);
            if (errorResult != null)
            {
                return errorResult;
            }

            bot!.IsPublic = request.IsPublic;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Bot {BotId} visibility updated to {IsPublic}", id, request.IsPublic);
            return Ok();
        }

        /// <summary>
        /// Updates bot token after validating it against Discord API.
        /// </summary>
        [Authorize]
        [HttpPut("admin/{id}/token")]
        public async Task<IActionResult> UpdateBotToken(int id, [FromBody] UpdateBotTokenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.BotToken))
            {
                return BadRequest("Bot token is required.");
            }

            var normalizedRawToken = _botTokenSecurityService.NormalizeRawToken(request.BotToken);
            if (string.IsNullOrWhiteSpace(normalizedRawToken))
            {
                return BadRequest("Bot token is invalid.");
            }

            var isAuthorized = await IsDiscordTokenAuthorizedAsync(normalizedRawToken);
            if (!isAuthorized)
            {
                return BadRequest("Bot token is not authorized by Discord API.");
            }

            var (bot, errorResult) = await GetBotOrNotFoundAsync(id);
            if (errorResult != null)
            {
                return errorResult;
            }

            bot!.BotToken = _botTokenSecurityService.ProtectForStorage(normalizedRawToken);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Bot {BotId} token updated after Discord authorization check", id);
            return Ok();
        }

        /// <summary>
        /// Starts the specified bot instance.
        /// </summary>
        [Authorize]
        [HttpPost("admin/{id}/start")]
        public async Task<IActionResult> StartBot(int id)
        {
            return await ExecuteLifecycleActionAsync(id, () => _botService.StartBotAsync(id), "started");
        }

        /// <summary>
        /// Stops the specified bot instance.
        /// </summary>
        [Authorize]
        [HttpPost("admin/{id}/stop")]
        public async Task<IActionResult> StopBot(int id)
        {
            return await ExecuteLifecycleActionAsync(id, () => _botService.StopBotAsync(id, "Ruční vypnutí"), "stopped");
        }

        /// <summary>
        /// Restarts the specified bot instance.
        /// </summary>
        [Authorize]
        [HttpPost("admin/{id}/restart")]
        public async Task<IActionResult> RestartBot(int id)
        {
            return await ExecuteLifecycleActionAsync(id, () => _botService.RestartBotAsync(id), "restarted");
        }

        /// <summary>
        /// Returns stored group data for a bot.
        /// </summary>
        [Authorize]
        [HttpGet("admin/{id}/groups")]
        public async Task<IActionResult> GetGroups(int id, [FromQuery] int? boardConfigurationId = null)
        {
            return await ExecuteBotReadActionAsync(
                id,
            () => _botService.GetBotGroupsAsync(id, boardConfigurationId),
                "Error fetching groups for bot {Id}",
                "Error fetching groups");
        }

        /// <summary>
        /// Saves group data for a bot.
        /// </summary>
        [Authorize]
        [HttpPost("admin/{id}/groups")]
        public async Task<IActionResult> SaveGroups(int id, [FromBody] BotGroupsDto groupsData, [FromQuery] int? boardConfigurationId = null)
        {
            return await ExecuteBotWriteActionAsync(
                id,
            () => _botService.SaveBotGroupsAsync(id, groupsData, boardConfigurationId),
                "Groups saved successfully",
                "Error saving groups for bot {Id}",
                "Error saving groups");
        }

        /// <summary>
        /// Returns team-shaped data mapped from stored groups.
        /// </summary>
        [Authorize]
        [HttpGet("admin/{id}/teams")]
        public async Task<IActionResult> GetTeams(int id, [FromQuery] int? boardConfigurationId = null)
        {
            return await ExecuteBotReadActionAsync(
                id,
            async () => GroupContractMapper.ToTeams(await _botService.GetBotGroupsAsync(id, boardConfigurationId)),
                "Error fetching teams for bot {Id}",
                "Error fetching teams");
        }

        /// <summary>
        /// Saves team-shaped data after mapping to group storage format.
        /// </summary>
        [Authorize]
        [HttpPost("admin/{id}/teams")]
        public async Task<IActionResult> SaveTeams(int id, [FromBody] BotTeamsDto teamsData, [FromQuery] int? boardConfigurationId = null)
        {
            return await ExecuteBotWriteActionAsync(
                id,
                () => _botService.SaveBotGroupsAsync(id, GroupContractMapper.FromTeams(teamsData), boardConfigurationId),
                "Teams saved successfully",
                "Error saving teams for bot {Id}",
                "Error saving teams");
        }

        /// <summary>
        /// Returns board configurations for a bot, optionally filtered by guild id.
        /// </summary>
        [Authorize]
        [HttpGet("admin/{id}/boards")]
        public async Task<IActionResult> GetBoards(int id, [FromQuery] string? guildId = null)
        {
            var (_, errorResult) = await GetBotOrNotFoundAsync(id);
            if (errorResult != null)
            {
                return errorResult;
            }

            ulong? parsedGuildId = null;
            if (!string.IsNullOrWhiteSpace(guildId) && ulong.TryParse(guildId, out var guildParsed))
            {
                parsedGuildId = guildParsed;
            }

            var botConfiguration = await _db.BotConfigurations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.BotId == id);

            var boardsQuery = _db.BoardConfigurations
                .AsNoTracking()
                .Where(c => c.BotId == id);

            if (parsedGuildId.HasValue)
            {
                boardsQuery = boardsQuery.Where(c => c.GuildId == parsedGuildId.Value);
            }

            var boardEntities = await boardsQuery
                .OrderBy(c => c.BoardConfigurationId)
                .ToListAsync();

            var boards = boardEntities
                .Select(c => new BoardConfigurationListItemDto
                {
                    BoardConfigurationId = c.BoardConfigurationId,
                    BotId = c.BotId,
                    BoardType = c.BoardType,
                    GuildId = c.GuildId?.ToString(),
                    BoardChannelId = c.BoardChannelId?.ToString(),
                    BoardMessageId = c.BoardMessageId?.ToString(),
                    BoardTitle = c.BoardTitle,
                    BoardDescriptionTemplate = c.BoardDescriptionTemplate,
                    SubtitleLabel = c.SubtitleLabel,
                    ContactLabel = c.ContactLabel,
                    IsActive = botConfiguration != null && botConfiguration.ActiveBoardConfigurationId == c.BoardConfigurationId
                })
                .ToList();

            return Ok(boards);
        }

        /// <summary>
        /// Creates a board configuration row for a bot.
        /// </summary>
        [Authorize]
        [HttpPost("admin/{id}/boards")]
        public async Task<IActionResult> CreateBoard(int id, [FromBody] CreateBoardConfigurationRequest request)
        {
            var (_, errorResult) = await GetBotOrNotFoundAsync(id);
            if (errorResult != null)
            {
                return errorResult;
            }

            var board = new BoardConfiguration
            {
                BotId = id,
                BoardType = string.IsNullOrWhiteSpace(request.BoardType) ? "teams" : request.BoardType.Trim(),
                GuildId = ParseNullableUlong(request.GuildId),
                BoardTitle = NormalizeNullable(request.BoardTitle),
                BoardDescriptionTemplate = NormalizeNullable(request.BoardDescriptionTemplate),
                SubtitleLabel = NormalizeNullable(request.SubtitleLabel),
                ContactLabel = NormalizeNullable(request.ContactLabel)
            };

            await _db.BoardConfigurations.AddAsync(board);
            await _db.SaveChangesAsync();

            var botConfiguration = await GetOrCreateBotConfigurationAsync(id);
            if (!botConfiguration.ActiveBoardConfigurationId.HasValue)
            {
                botConfiguration.ActiveBoardConfigurationId = board.BoardConfigurationId;
                await _db.SaveChangesAsync();
            }

            return Ok(new { boardConfigurationId = board.BoardConfigurationId });
        }

        /// <summary>
        /// Sets the active board configuration for a bot.
        /// </summary>
        [Authorize]
        [HttpPut("admin/{id}/boards/active")]
        public async Task<IActionResult> SetActiveBoard(int id, [FromBody] SetActiveBoardRequest request)
        {
            var (_, errorResult) = await GetBotOrNotFoundAsync(id);
            if (errorResult != null)
            {
                return errorResult;
            }

            var boardExists = await _db.BoardConfigurations
                .AnyAsync(c => c.BotId == id && c.BoardConfigurationId == request.BoardConfigurationId);
            if (!boardExists)
            {
                return NotFound();
            }

            var botConfiguration = await GetOrCreateBotConfigurationAsync(id);
            botConfiguration.ActiveBoardConfigurationId = request.BoardConfigurationId;
            await _db.SaveChangesAsync();

            return Ok();
        }

        /// <summary>
        /// Deletes a board configuration and any board-scoped teams bound to it.
        /// </summary>
        [Authorize]
        [HttpDelete("admin/{id}/boards/{boardConfigurationId}")]
        public async Task<IActionResult> DeleteBoard(int id, int boardConfigurationId)
        {
            var (_, errorResult) = await GetBotOrNotFoundAsync(id);
            if (errorResult != null)
            {
                return errorResult;
            }

            var board = await _db.BoardConfigurations
                .FirstOrDefaultAsync(c => c.BotId == id && c.BoardConfigurationId == boardConfigurationId);
            if (board == null)
            {
                return NotFound();
            }

            var botConfiguration = await _db.BotConfigurations
                .FirstOrDefaultAsync(c => c.BotId == id);
            if (botConfiguration?.ActiveBoardConfigurationId == boardConfigurationId)
            {
                var nextBoardId = await _db.BoardConfigurations
                    .Where(c => c.BotId == id && c.BoardConfigurationId != boardConfigurationId)
                    .OrderBy(c => c.BoardConfigurationId)
                    .Select(c => (int?)c.BoardConfigurationId)
                    .FirstOrDefaultAsync();

                botConfiguration.ActiveBoardConfigurationId = nextBoardId;
                await _db.SaveChangesAsync();
            }

            _db.BoardConfigurations.Remove(board);
            await _db.SaveChangesAsync();

            return Ok();
        }

        /// <summary>
        /// Updates board configuration fields for a specific board.
        /// </summary>
        [Authorize]
        [HttpPut("admin/{id}/boards/{boardConfigurationId}")]
        public async Task<IActionResult> UpdateBoard(int id, int boardConfigurationId, [FromBody] UpdateBoardConfigurationRequest request)
        {
            var board = await _db.BoardConfigurations
                .FirstOrDefaultAsync(c => c.BotId == id && c.BoardConfigurationId == boardConfigurationId);
            if (board == null)
            {
                return NotFound();
            }

            board.BoardType = string.IsNullOrWhiteSpace(request.BoardType) ? "teams" : request.BoardType.Trim();
            board.BoardChannelId = ParseNullableUlong(request.BoardChannelId);
            board.BoardMessageId = ParseNullableUlong(request.BoardMessageId);
            board.BoardTitle = NormalizeNullable(request.BoardTitle);
            board.BoardDescriptionTemplate = NormalizeNullable(request.BoardDescriptionTemplate);
            board.SubtitleLabel = NormalizeNullable(request.SubtitleLabel);
            board.ContactLabel = NormalizeNullable(request.ContactLabel);

            await _db.SaveChangesAsync();
            return Ok();
        }

        /// <summary>
        /// Deletes system and command usage logs related to a bot.
        /// </summary>
        [Authorize]
        [HttpDelete("admin/{id}/logs")]
        public async Task<IActionResult> ClearBotLogs(int id)
        {
            var (bot, errorResult) = await GetBotOrNotFoundAsync(id);
            if (errorResult != null)
            {
                return errorResult;
            }

            var botMarker = $"BotId={bot!.BotId}";

            var systemLogsQuery = _db.SystemLogs
                .Where(l => l.Message.Contains(botMarker));

            var commandLogsQuery = _db.CommandUsageLogs
                .Where(l => _db.BotCommands
                    .Where(c => c.BotId == id)
                    .Select(c => c.CommandId)
                    .Contains(l.CommandId));

            var systemLogsCount = await systemLogsQuery.CountAsync();
            var commandLogsCount = await commandLogsQuery.CountAsync();

            if (systemLogsCount > 0)
            {
                await systemLogsQuery.ExecuteDeleteAsync();
            }

            if (commandLogsCount > 0)
            {
                await commandLogsQuery.ExecuteDeleteAsync();
            }

            _logger.LogInformation("Cleared logs for bot {BotId}. SystemLogs={SystemCount}, CommandLogs={CommandCount}",
                id,
                systemLogsCount,
                commandLogsCount);

            return Ok(new { removedSystemLogs = systemLogsCount, removedCommandLogs = commandLogsCount });
        }

        /// <summary>
        /// Deletes run history entries for a bot.
        /// </summary>
        [Authorize]
        [HttpDelete("admin/{id}/history")]
        public async Task<IActionResult> ClearBotRunHistory(int id)
        {
            var (_, errorResult) = await GetBotOrNotFoundAsync(id);
            if (errorResult != null)
            {
                return errorResult;
            }

            var historiesQuery = _db.BotRunHistories
                .Where(h => h.BotId == id);

            var removedCount = await historiesQuery.CountAsync();
            if (removedCount > 0)
            {
                await historiesQuery.ExecuteDeleteAsync();
            }

            _logger.LogInformation("Cleared run history for bot {BotId}. Histories={Count}", id, removedCount);

            return Ok(new { removedHistories = removedCount });
        }

        /// <summary>
        /// Loads merged system and command logs for a bot.
        /// </summary>
        private async Task<List<BotLogDto>> LoadBotLogsAsync(Bot bot, int take)
        {
            var botMarker = $"BotId={bot.BotId}";

            var systemLogs = await _db.SystemLogs
                .AsNoTracking()
                .Where(l =>
                    l.Category.Contains("Bot") ||
                    l.Message.Contains(bot.Name) ||
                    l.Message.Contains(botMarker))
                .OrderByDescending(l => l.Timestamp)
                .Take(take)
                .Select(l => new BotLogDto { Timestamp = l.Timestamp, Level = l.Level, Message = l.Message })
                .ToListAsync();

            var commandRows = await (from log in _db.CommandUsageLogs.AsNoTracking()
                                     join command in _db.BotCommands.AsNoTracking()
                                         on log.CommandId equals command.CommandId
                                     where command.BotId == bot.BotId
                                     orderby log.ExecutedAt descending
                                     select new
                                     {
                                         log.ExecutedAt,
                                         log.IsSuccess,
                                         log.UserId,
                                         log.UserName,
                                         log.ErrorMessage,
                                         command.CommandName,
                                         command.SubCommandName
                                     })
                .Take(take)
                .ToListAsync();

            var commandLogs = commandRows
                .Select(row => new BotLogDto
                {
                    Timestamp = row.ExecutedAt,
                    Level = row.IsSuccess ? "Information" : "Warning",
                    Message = row.IsSuccess
                        ? $"{FormatCommandLabel(row.CommandName, row.SubCommandName)} by {row.UserName ?? row.UserId.ToString()} - OK"
                        : $"{FormatCommandLabel(row.CommandName, row.SubCommandName)} by {row.UserName ?? row.UserId.ToString()} - FAILED: {row.ErrorMessage ?? "Unknown error"}"
                })
                .ToList();

            return systemLogs
                .Concat(commandLogs)
                .OrderByDescending(l => l.Timestamp)
                .Take(take)
                .ToList();
        }

        /// <summary>
        /// Loads per-bot request and error counters from the last 24 hours of command usage.
        /// </summary>
        private async Task<Dictionary<int, (int Requests24h, int Errors24h)>> LoadUsageStats24hAsync()
        {
            var from = DateTime.UtcNow.AddHours(-24);

            return await _db.CommandUsageLogs
                .AsNoTracking()
                .Where(log => log.ExecutedAt >= from)
                .Join(
                    _db.BotCommands.AsNoTracking(),
                    log => log.CommandId,
                    command => command.CommandId,
                    (log, command) => new { log.IsSuccess, command.BotId })
                .GroupBy(x => x.BotId)
                .Select(group => new
                {
                    BotId = group.Key,
                    Requests24h = group.Count(),
                    Errors24h = group.Count(x => !x.IsSuccess)
                })
                .ToDictionaryAsync(
                    row => row.BotId,
                    row => (row.Requests24h, row.Errors24h));
        }

        /// <summary>
        /// Loads a bot by id or returns a NotFound action result.
        /// </summary>
        private async Task<(Bot? Bot, IActionResult? ErrorResult)> GetBotOrNotFoundAsync(int id)
        {
            var bot = await _db.Bots.FirstOrDefaultAsync(b => b.BotId == id);
            return bot == null
                ? (null, NotFound())
                : (bot, null);
        }

        /// <summary>
        /// Gets or creates the BotConfiguration row for a bot.
        /// </summary>
        private async Task<BotConfiguration> GetOrCreateBotConfigurationAsync(int botId)
        {
            var botConfiguration = await _db.BotConfigurations
                .FirstOrDefaultAsync(c => c.BotId == botId);

            if (botConfiguration != null)
            {
                return botConfiguration;
            }

            botConfiguration = new BotConfiguration { BotId = botId };
            _db.BotConfigurations.Add(botConfiguration);
            await _db.SaveChangesAsync();
            return botConfiguration;
        }

        /// <summary>
        /// Parses nullable ulong from input text.
        /// </summary>
        private static ulong? ParseNullableUlong(string? value)
        {
            return ulong.TryParse(value, out var parsed) ? parsed : null;
        }

        /// <summary>
        /// Normalizes string values to null when empty.
        /// </summary>
        private static string? NormalizeNullable(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private bool TryResolveRawToken(string storedToken, out string? rawToken)
        {
            rawToken = null;
            if (!_botTokenSecurityService.TryGetRawToken(storedToken, out var extracted))
            {
                return false;
            }

            rawToken = extracted;
            return !string.IsNullOrWhiteSpace(rawToken);
        }

        private async Task<bool> IsDiscordTokenAuthorizedAsync(string rawToken)
        {
            var identity = await _discordBotIdentityService.GetIdentityAsync(rawToken);
            return identity != null;
        }

        private static string FormatCommandLabel(BotCommand cmd)
        {
            return FormatCommandLabel(cmd.CommandName, cmd.SubCommandName);
        }

        private static string FormatCommandLabel(string commandName, string? subCommandName)
        {
            if (commandName.StartsWith("reaction-", StringComparison.OrdinalIgnoreCase))
            {
                return $"[{commandName}]";
            }

            return $"/{commandName}{(string.IsNullOrWhiteSpace(subCommandName) ? string.Empty : $" {subCommandName}")}";
        }

        /// <summary>
        /// Executes bot lifecycle actions with shared NotFound and success response handling.
        /// </summary>
        private async Task<IActionResult> ExecuteLifecycleActionAsync(int id, Func<Task<bool>> action, string state)
        {
            var success = await action();
            if (!success)
            {
                return NotFound();
            }

            return Ok(new { message = $"Bot {id} {state}." });
        }

        /// <summary>
        /// Executes a read action with shared NotFound and error handling.
        /// </summary>
        private async Task<IActionResult> ExecuteBotReadActionAsync<T>(
            int id,
            Func<Task<T>> action,
            string logMessage,
            string errorMessage)
        {
            try
            {
                var result = await action();
                return Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, logMessage, id);
                return StatusCode(500, new { message = errorMessage });
            }
        }

        /// <summary>
        /// Executes a write action with shared success and error handling.
        /// </summary>
        private async Task<IActionResult> ExecuteBotWriteActionAsync(
            int id,
            Func<Task<bool>> action,
            string successMessage,
            string logMessage,
            string errorMessage)
        {
            try
            {
                var success = await action();
                if (!success)
                {
                    return StatusCode(500, new { message = errorMessage });
                }

                return Ok(new { message = successMessage });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, logMessage, id);
                return StatusCode(500, new { message = errorMessage });
            }
        }

        /// <summary>
        /// Request payload for bot visibility updates.
        /// </summary>
        public sealed class UpdateBotVisibilityRequest
        {
            public bool IsPublic { get; set; }
        }

        public sealed class UpdateBotTokenRequest
        {
            public string BotToken { get; set; } = string.Empty;
        }

        public sealed class CreateBoardConfigurationRequest
        {
            public string? BoardType { get; set; }
            public string? GuildId { get; set; }
            public string? BoardTitle { get; set; }
            public string? BoardDescriptionTemplate { get; set; }
            public string? SubtitleLabel { get; set; }
            public string? ContactLabel { get; set; }
        }

        public sealed class UpdateBoardConfigurationRequest
        {
            public string? BoardType { get; set; }
            [System.Text.Json.Serialization.JsonConverter(typeof(FlexibleStringConverter))]
            public string? BoardChannelId { get; set; }
            [System.Text.Json.Serialization.JsonConverter(typeof(FlexibleStringConverter))]
            public string? BoardMessageId { get; set; }
            public string? BoardTitle { get; set; }
            public string? BoardDescriptionTemplate { get; set; }
            public string? SubtitleLabel { get; set; }
            public string? ContactLabel { get; set; }
        }

        public sealed class SetActiveBoardRequest
        {
            public int BoardConfigurationId { get; set; }
        }

        public sealed class BoardConfigurationListItemDto
        {
            public int BoardConfigurationId { get; set; }
            public int BotId { get; set; }
            public string BoardType { get; set; } = "teams";
            public string? GuildId { get; set; }
            public string? BoardChannelId { get; set; }
            public string? BoardMessageId { get; set; }
            public string? BoardTitle { get; set; }
            public string? BoardDescriptionTemplate { get; set; }
            public string? SubtitleLabel { get; set; }
            public string? ContactLabel { get; set; }
            public bool IsActive { get; set; }
        }
    }

    /// <summary>
    /// Converts JSON strings or numbers to C# strings, supporting Discord IDs as large numbers.
    /// </summary>
    public sealed class FlexibleStringConverter : System.Text.Json.Serialization.JsonConverter<string>
    {
        public override string? Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                System.Text.Json.JsonTokenType.String => reader.GetString(),
                System.Text.Json.JsonTokenType.Number => reader.TryGetInt64(out var longValue) ? longValue.ToString() : reader.GetDecimal().ToString(),
                System.Text.Json.JsonTokenType.Null => null,
                _ => throw new System.Text.Json.JsonException($"Unexpected token type: {reader.TokenType}")
            };
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, string? value, System.Text.Json.JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(value);
            }
        }
    }
}
