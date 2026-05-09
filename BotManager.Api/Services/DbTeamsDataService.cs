using System.Text.Json;
using BotManager.Api.Models;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Api.Services
{
    public class DbTeamsDataService : ITeamsDataService
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
    }
}
