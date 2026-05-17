using BotManager.Backend.Shared.Models;
using BotManager.Backend.Shared.Services;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Stores and loads bot configuration data from the database.
    /// </summary>
    public class DbBotDataService : IBotDataService
    {
        private readonly BotManagerDbContext _db;

        /// <summary>
        /// Creates a new database-backed bot data service.
        /// </summary>
        public DbBotDataService(BotManagerDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Retrieves bot configuration for a specific bot id.
        /// </summary>
        public async Task<BotConfigurationDto> GetAsync(int botId)
        {
            var botConfiguration = await _db.BotConfigurations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.BotId == botId);

            var boardsQuery = _db.BoardConfigurations
                .AsNoTracking()
                .Where(c => c.BotId == botId);

            BoardConfiguration? config;
            if (botConfiguration?.ActiveBoardConfigurationId != null)
            {
                config = await boardsQuery
                    .FirstOrDefaultAsync(c => c.BoardConfigurationId == botConfiguration.ActiveBoardConfigurationId.Value);

                config ??= await boardsQuery
                    .OrderBy(c => c.BoardConfigurationId)
                    .FirstOrDefaultAsync();
            }
            else
            {
                config = await boardsQuery
                    .OrderBy(c => c.BoardConfigurationId)
                    .FirstOrDefaultAsync();
            }

            var globalConfig = await _db.BoardGlobalConfigs
                .AsNoTracking()
                .OrderBy(c => c.Id)
                .FirstOrDefaultAsync();

            if (config == null)
            {
                return new BotConfigurationDto
                {
                    BoardTitle = globalConfig?.DefaultBoardTitle,
                    BoardDescriptionTemplate = globalConfig?.DefaultBoardDescription,
                    SubtitleLabel = globalConfig?.DefaultBoardDetailSubtitleLabel,
                    ContactLabel = globalConfig?.DefaultBoardDetailContactLabel
                };
            }

            return new BotConfigurationDto
            {
                ActiveBoardConfigurationId = botConfiguration?.ActiveBoardConfigurationId ?? config.BoardConfigurationId,
                BoardType = config.BoardType,
                BoardChannelId = config.BoardChannelId?.ToString(),
                BoardMessageId = config.BoardMessageId?.ToString(),
                BoardTitle = config.BoardTitle ?? globalConfig?.DefaultBoardTitle,
                BoardDescriptionTemplate = config.BoardDescriptionTemplate ?? globalConfig?.DefaultBoardDescription,
                SubtitleLabel = config.SubtitleLabel ?? globalConfig?.DefaultBoardDetailSubtitleLabel,
                ContactLabel = config.ContactLabel ?? globalConfig?.DefaultBoardDetailContactLabel
            };
        }

        /// <summary>
        /// Saves bot configuration for a specific bot id.
        /// </summary>
        public async Task SaveAsync(int botId, BotConfigurationDto data)
        {
            var botConfiguration = await _db.BotConfigurations
                .FirstOrDefaultAsync(c => c.BotId == botId);

            if (botConfiguration == null)
            {
                botConfiguration = new BotConfiguration { BotId = botId };
                _db.BotConfigurations.Add(botConfiguration);
            }

            var boardsQuery = _db.BoardConfigurations
                .Where(c => c.BotId == botId);

            BoardConfiguration? config = null;

            if (data.ActiveBoardConfigurationId.HasValue)
            {
                config = await boardsQuery
                    .FirstOrDefaultAsync(c => c.BoardConfigurationId == data.ActiveBoardConfigurationId.Value);
            }

            if (config == null && botConfiguration.ActiveBoardConfigurationId.HasValue)
            {
                config = await boardsQuery
                    .FirstOrDefaultAsync(c => c.BoardConfigurationId == botConfiguration.ActiveBoardConfigurationId.Value);
            }

            config ??= await boardsQuery
                .OrderBy(c => c.BoardConfigurationId)
                .FirstOrDefaultAsync();

            var boardChannelId = ParseNullableUlong(data.BoardChannelId);
            var boardMessageId = ParseNullableUlong(data.BoardMessageId);

            if (config == null)
            {
                config = new BoardConfiguration
                {
                    BotId = botId,
                    BoardType = NormalizeBoardType(data.BoardType),
                    BoardChannelId = boardChannelId,
                    BoardMessageId = boardMessageId,
                    BoardTitle = NormalizeNullable(data.BoardTitle),
                    BoardDescriptionTemplate = NormalizeNullable(data.BoardDescriptionTemplate),
                    SubtitleLabel = NormalizeNullable(data.SubtitleLabel),
                    ContactLabel = NormalizeNullable(data.ContactLabel)
                };
                _db.BoardConfigurations.Add(config);
            }
            else
            {
                config.BoardType = NormalizeBoardType(data.BoardType);
                config.BoardChannelId = boardChannelId;
                config.BoardMessageId = boardMessageId;
                config.BoardTitle = NormalizeNullable(data.BoardTitle);
                config.BoardDescriptionTemplate = NormalizeNullable(data.BoardDescriptionTemplate);
                config.SubtitleLabel = NormalizeNullable(data.SubtitleLabel);
                config.ContactLabel = NormalizeNullable(data.ContactLabel);
            }

            await _db.SaveChangesAsync();

            var targetActiveBoardId = config.BoardConfigurationId;
            if (data.ActiveBoardConfigurationId.HasValue && data.ActiveBoardConfigurationId.Value > 0)
            {
                var activeExists = await _db.BoardConfigurations
                    .AnyAsync(c => c.BotId == botId && c.BoardConfigurationId == data.ActiveBoardConfigurationId.Value);
                if (activeExists)
                {
                    targetActiveBoardId = data.ActiveBoardConfigurationId.Value;
                }
            }

            if (botConfiguration.ActiveBoardConfigurationId != targetActiveBoardId)
            {
                botConfiguration.ActiveBoardConfigurationId = targetActiveBoardId;
                await _db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Parses a numeric Discord identifier and returns null for invalid input.
        /// </summary>
        private static ulong? ParseNullableUlong(string? value)
        {
            return ulong.TryParse(value, out var parsed) ? parsed : null;
        }

        /// <summary>
        /// Normalizes board type to a persisted default.
        /// </summary>
        private static string NormalizeBoardType(string? boardType)
        {
            return string.IsNullOrWhiteSpace(boardType) ? "teams" : boardType.Trim();
        }

        /// <summary>
        /// Returns null for empty values, otherwise trimmed text.
        /// </summary>
        private static string? NormalizeNullable(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
