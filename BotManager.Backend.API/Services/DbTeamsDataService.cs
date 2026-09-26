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
                    Emoji = t.Emoji,
                    RoleId = t.RoleId
                })
                .ToListAsync();

            return new BotTeamsDto { Teams = teams };
        }

        /// <summary>
        /// Synchronizes the board's persisted teams with the given set: rows matched by TeamId are updated in place
        /// (keeping their TeamId and role binding), teams without a known TeamId are inserted and rows that are no
        /// longer present are deleted.
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
                    Emoji = string.IsNullOrWhiteSpace(team.Emoji) ? "🎯" : team.Emoji.Trim(),
                    RoleId = team.RoleId
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

            var existingTeams = await _db.Teams
                .Where(t => t.BoardConfigurationId == boardConfiguration.BoardConfigurationId)
                .ToListAsync();

            // Only rows of this board can be matched by id; an id of another board's team is treated as a new team.
            var existingById = existingTeams.ToDictionary(t => t.TeamId);
            var matchedTeamIds = new HashSet<int>();
            var teamsToInsert = new List<TeamDto>();

            foreach (var teamDto in normalizedTeams)
            {
                if (teamDto.TeamId is int teamId
                    && existingById.TryGetValue(teamId, out var existing)
                    && matchedTeamIds.Add(teamId))
                {
                    existing.Name = teamDto.Name;
                    existing.LeaderName = teamDto.LeaderName;
                    existing.CommanderContact = teamDto.Contact;
                    existing.Emoji = teamDto.Emoji;
                    // Clients never send RoleId; keep the stored binding unless a caller sets one explicitly.
                    existing.RoleId = teamDto.RoleId ?? existing.RoleId;
                }
                else
                {
                    teamsToInsert.Add(teamDto);
                }
            }

            var removedTeams = existingTeams
                .Where(t => !matchedTeamIds.Contains(t.TeamId))
                .ToList();
            _db.Teams.RemoveRange(removedTeams);

            // A new row may replace a removed one (e.g. a client that dropped the id); keep its role binding by name.
            var removedRoleIdsByName = removedTeams
                .Where(t => t.RoleId.HasValue)
                .GroupBy(t => t.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().RoleId, StringComparer.Ordinal);

            foreach (var teamDto in teamsToInsert)
            {
                _db.Teams.Add(new Team
                {
                    BoardConfigurationId = boardConfiguration.BoardConfigurationId,
                    Name = teamDto.Name,
                    LeaderName = teamDto.LeaderName,
                    CommanderContact = teamDto.Contact,
                    Emoji = teamDto.Emoji,
                    RoleId = teamDto.RoleId
                        ?? (removedRoleIdsByName.TryGetValue(teamDto.Name, out var byName) ? byName : null)
                });
            }

            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Binds a team to a Discord role id.
        /// </summary>
        public async Task SetRoleIdAsync(int teamId, ulong? roleId)
        {
            await _db.Teams
                .Where(t => t.TeamId == teamId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RoleId, roleId));
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
