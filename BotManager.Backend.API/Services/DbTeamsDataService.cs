using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Shared.Models;
using BotManager.Backend.Shared.Services;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Stores and loads team data from the database and maps it to shared DTOs.
    /// </summary>
    public class DbTeamsDataService : ITeamsDataService, IGroupDataService
    {
        private readonly BotManagerDbContext _db;

        /// <summary>
        /// Creates a new database-backed teams data service.
        /// </summary>
        public DbTeamsDataService(BotManagerDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Loads all teams for bot features from the teams table.
        /// </summary>
        public async Task<BotTeamsDto> GetAsync(int botId, int? boardConfigurationId = null)
        {
            var boardConfiguration = await ResolveBoardConfigurationAsync(botId, boardConfigurationId);

            var teams = await _db.Teams
                .AsNoTracking()
                .Where(t => t.BoardConfigurationId == boardConfiguration.BoardConfigurationId)
                .Select(t => new TeamDto
                {
                    TeamId = t.TeamId,
                    Name = t.Name,
                    LeaderName = t.LeaderName,
                    Contact = t.CommanderContact,
                    Emoji = t.Emoji
                })
                .ToListAsync();

            return new BotTeamsDto { Teams = teams };
        }

        /// <summary>
        /// Replaces persisted teams with a new set of team records.
        /// </summary>
        public async Task SaveAsync(int botId, BotTeamsDto data, int? boardConfigurationId = null)
        {
            var boardConfiguration = await ResolveBoardConfigurationAsync(botId, boardConfigurationId);

            var normalizedTeams = data.Teams
                .Select(team => new TeamDto
                {
                    TeamId = team.TeamId,
                    Name = team.Name,
                    LeaderName = team.LeaderName,
                    Contact = team.Contact,
                    Emoji = string.IsNullOrWhiteSpace(team.Emoji) ? "🎯" : team.Emoji.Trim()
                })
                .ToList();

            var duplicateEmojis = normalizedTeams
                .GroupBy(team => team.Emoji, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            if (duplicateEmojis.Count > 0)
            {
                throw new InvalidOperationException($"Duplicate team emojis are not allowed on one board: {string.Join(", ", duplicateEmojis)}");
            }

            // Clear existing teams
            var existingTeams = await _db.Teams
                .Where(t => t.BoardConfigurationId == boardConfiguration.BoardConfigurationId)
                .ToListAsync();
            _db.Teams.RemoveRange(existingTeams);

            // Add new teams
            foreach (var teamDto in normalizedTeams)
            {
                var team = new Team
                {
                    BoardConfigurationId = boardConfiguration.BoardConfigurationId,
                    Name = teamDto.Name,
                    LeaderName = teamDto.LeaderName,
                    CommanderContact = teamDto.Contact,
                    Emoji = teamDto.Emoji
                };
                _db.Teams.Add(team);
            }

            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Loads group DTOs by mapping stored teams.
        /// </summary>
        async Task<BotGroupsDto> IGroupDataService.GetAsync(int botId, int? boardConfigurationId)
        {
            var teams = await GetAsync(botId, boardConfigurationId);
            return GroupContractMapper.FromTeams(teams);
        }

        /// <summary>
        /// Saves group DTOs by mapping them to teams.
        /// </summary>
        async Task IGroupDataService.SaveAsync(int botId, BotGroupsDto data, int? boardConfigurationId)
        {
            await SaveAsync(botId, GroupContractMapper.ToTeams(data), boardConfigurationId);
        }

        /// <summary>
        /// Resolves a board for the bot by explicit id or configured active board, creating one if missing.
        /// </summary>
        private async Task<BoardConfiguration> ResolveBoardConfigurationAsync(int botId, int? boardConfigurationId)
        {
            if (boardConfigurationId.HasValue)
            {
                var explicitBoard = await _db.BoardConfigurations
                    .FirstOrDefaultAsync(c => c.BotId == botId && c.BoardConfigurationId == boardConfigurationId.Value);

                if (explicitBoard == null)
                {
                    throw new KeyNotFoundException($"Board {boardConfigurationId.Value} not found for bot {botId}");
                }

                return explicitBoard;
            }

            var botConfiguration = await _db.BotConfigurations
                .FirstOrDefaultAsync(c => c.BotId == botId);

            BoardConfiguration? boardConfiguration = null;
            if (botConfiguration?.ActiveBoardConfigurationId != null)
            {
                boardConfiguration = await _db.BoardConfigurations
                    .FirstOrDefaultAsync(c => c.BotId == botId && c.BoardConfigurationId == botConfiguration.ActiveBoardConfigurationId.Value);
            }

            boardConfiguration ??= await _db.BoardConfigurations
                .Where(c => c.BotId == botId)
                .OrderBy(c => c.BoardConfigurationId)
                .FirstOrDefaultAsync();

            if (boardConfiguration != null)
            {
                return boardConfiguration;
            }

            boardConfiguration = new BoardConfiguration
            {
                BotId = botId,
                BoardType = "teams"
            };

            _db.BoardConfigurations.Add(boardConfiguration);
            await _db.SaveChangesAsync();

            if (botConfiguration == null)
            {
                botConfiguration = new BotConfiguration { BotId = botId };
                _db.BotConfigurations.Add(botConfiguration);
            }

            botConfiguration.ActiveBoardConfigurationId = boardConfiguration.BoardConfigurationId;
            await _db.SaveChangesAsync();

            return boardConfiguration;
        }
    }
}
