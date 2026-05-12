using System.Text.Json;
using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Shared.Models;
using BotManager.Backend.Shared.Services;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Services
{
    public class DbTeamsDataService : ITeamsDataService, IGroupDataService
    {
        private readonly BotManagerDbContext _db;

        public DbTeamsDataService(BotManagerDbContext db)
        {
            _db = db;
        }

        public async Task<BotTeamsDto> GetAsync(int botId)
        {
            var teams = await _db.Teams
                .AsNoTracking()
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

        public async Task SaveAsync(int botId, BotTeamsDto data)
        {
            // Clear existing teams
            var existingTeams = _db.Teams.ToList();
            _db.Teams.RemoveRange(existingTeams);

            // Add new teams
            foreach (var teamDto in data.Teams)
            {
                var team = new Team
                {
                    Name = teamDto.Name,
                    LeaderName = teamDto.LeaderName,
                    CommanderContact = teamDto.Contact,
                    Emoji = teamDto.Emoji
                };
                _db.Teams.Add(team);
            }

            await _db.SaveChangesAsync();
        }

        async Task<BotGroupsDto> IGroupDataService.GetAsync(int botId)
        {
            var teams = await GetAsync(botId);
            return GroupContractMapper.FromTeams(teams);
        }

        async Task IGroupDataService.SaveAsync(int botId, BotGroupsDto data)
        {
            await SaveAsync(botId, GroupContractMapper.ToTeams(data));
        }
    }
}
