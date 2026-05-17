using BotManager.Backend.API.Services;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    /// <summary>
    /// Provides administrative endpoints for reading and updating system configuration.
    /// </summary>
    public class SystemConfigController : ControllerBase
    {
        private const string GlobalBoardTitleKey = "BoardGlobal.DefaultBoardTitle";
        private const string GlobalBoardDescriptionKey = "BoardGlobal.DefaultBoardDescription";
        private const string GlobalBoardSubtitleLabelKey = "BoardGlobal.DefaultBoardDetailSubtitleLabel";
        private const string GlobalBoardContactLabelKey = "BoardGlobal.DefaultBoardDetailContactLabel";

        private readonly BotManagerDbContext _db;
        private readonly ISystemConfigService _configService;
        private readonly ILogger<SystemConfigController> _logger;

        /// <summary>
        /// Creates a new system configuration controller.
        /// </summary>
        public SystemConfigController(BotManagerDbContext db, ISystemConfigService configService, ILogger<SystemConfigController> logger)
        {
            _db = db;
            _configService = configService;
            _logger = logger;
        }

        /// <summary>
        /// Returns all persisted system configuration key-value pairs.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var configs = await _configService.GetAllAsync();
            var boardGlobalConfig = await GetOrCreateBoardGlobalConfigAsync();

            var all = configs
                .Select(c => new ConfigListItemDto
                {
                    Id = c.Id,
                    Key = c.Key,
                    Value = c.Value,
                    Description = c.Description
                })
                .ToList();

            all.Add(new ConfigListItemDto
            {
                Id = boardGlobalConfig.Id,
                Key = GlobalBoardTitleKey,
                Value = boardGlobalConfig.DefaultBoardTitle ?? string.Empty,
                Description = "Default board title used when board-level title is empty"
            });
            all.Add(new ConfigListItemDto
            {
                Id = boardGlobalConfig.Id,
                Key = GlobalBoardDescriptionKey,
                Value = boardGlobalConfig.DefaultBoardDescription ?? string.Empty,
                Description = "Default board description template used when board-level value is empty"
            });
            all.Add(new ConfigListItemDto
            {
                Id = boardGlobalConfig.Id,
                Key = GlobalBoardSubtitleLabelKey,
                Value = boardGlobalConfig.DefaultBoardDetailSubtitleLabel ?? string.Empty,
                Description = "Default subtitle label used in board details"
            });
            all.Add(new ConfigListItemDto
            {
                Id = boardGlobalConfig.Id,
                Key = GlobalBoardContactLabelKey,
                Value = boardGlobalConfig.DefaultBoardDetailContactLabel ?? string.Empty,
                Description = "Default contact label used in board details"
            });

            return Ok(all.OrderBy(c => c.Key));
        }

        /// <summary>
        /// Updates a single system configuration value by key.
        /// </summary>
        [HttpPut]
        public async Task<IActionResult> Update([FromBody] ConfigUpdateDto dto)
        {
            if (TryMapBoardGlobalKey(dto.Key, out var targetField))
            {
                var boardGlobalConfig = await GetOrCreateBoardGlobalConfigAsync();
                var value = NormalizeNullable(dto.Value);

                switch (targetField)
                {
                    case BoardGlobalField.Title:
                        boardGlobalConfig.DefaultBoardTitle = value;
                        break;
                    case BoardGlobalField.Description:
                        boardGlobalConfig.DefaultBoardDescription = value;
                        break;
                    case BoardGlobalField.SubtitleLabel:
                        boardGlobalConfig.DefaultBoardDetailSubtitleLabel = value;
                        break;
                    case BoardGlobalField.ContactLabel:
                        boardGlobalConfig.DefaultBoardDetailContactLabel = value;
                        break;
                }

                await _db.SaveChangesAsync();
                _logger.LogInformation("Board global config updated: {Key} = {Value}", dto.Key, dto.Value);
                return Ok();
            }

            await _configService.SetValueAsync(dto.Key, dto.Value);
            _logger.LogInformation("System config updated: {Key} = {Value}", dto.Key, dto.Value);
            return Ok();
        }

        /// <summary>
        /// Gets board global config row or creates a default one.
        /// </summary>
        private async Task<BoardGlobalConfig> GetOrCreateBoardGlobalConfigAsync()
        {
            var config = await _db.BoardGlobalConfigs.FirstOrDefaultAsync(c => c.Id == 1);
            if (config != null)
            {
                return config;
            }

            config = new BoardGlobalConfig
            {
                Id = 1,
                DefaultBoardTitle = "📋 Seznam všech skupin",
                DefaultBoardDescription = "Celkem registrovaných skupin: {count}",
                DefaultBoardDetailSubtitleLabel = "Podtitul",
                DefaultBoardDetailContactLabel = "Kontakt"
            };

            _db.BoardGlobalConfigs.Add(config);
            await _db.SaveChangesAsync();
            return config;
        }

        /// <summary>
        /// Maps config keys used by the system-config endpoint to board-global fields.
        /// </summary>
        private static bool TryMapBoardGlobalKey(string key, out BoardGlobalField field)
        {
            field = key switch
            {
                GlobalBoardTitleKey => BoardGlobalField.Title,
                GlobalBoardDescriptionKey => BoardGlobalField.Description,
                GlobalBoardSubtitleLabelKey => BoardGlobalField.SubtitleLabel,
                GlobalBoardContactLabelKey => BoardGlobalField.ContactLabel,
                _ => BoardGlobalField.Unknown
            };

            return field != BoardGlobalField.Unknown;
        }

        /// <summary>
        /// Converts empty string values to null before persistence.
        /// </summary>
        private static string? NormalizeNullable(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private sealed class ConfigListItemDto
        {
            public int Id { get; set; }
            public string Key { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public string? Description { get; set; }
        }

        private enum BoardGlobalField
        {
            Unknown = 0,
            Title = 1,
            Description = 2,
            SubtitleLabel = 3,
            ContactLabel = 4
        }
    }

    public class ConfigUpdateDto
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
