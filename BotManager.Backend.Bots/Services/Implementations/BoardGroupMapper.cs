using BotManager.Backend.Shared.Models;
using BotManager.Backend.Bots.Models;

namespace BotManager.Backend.Bots.Services.Implementations
{
    public static class BoardGroupMapper
    {
        public static BoardGroupCollectionDto FromTeams(BotTeamsDto teamsData)
        {
            return new BoardGroupCollectionDto
            {
                Groups = teamsData.Teams.Select(team => new BoardGroupDto
                {
                    Id = team.TeamId,
                    Name = team.Name,
                    Subtitle = team.LeaderName,
                    Contact = team.Contact,
                    Emoji = team.Emoji
                }).ToList()
            };
        }

        public static BotTeamsDto ToTeams(BoardGroupCollectionDto groupsData)
        {
            return new BotTeamsDto
            {
                Teams = groupsData.Groups.Select(group => new TeamDto
                {
                    TeamId = group.Id,
                    Name = group.Name,
                    LeaderName = group.Subtitle ?? string.Empty,
                    Contact = group.Contact ?? string.Empty,
                    Emoji = group.Emoji ?? string.Empty
                }).ToList()
            };
        }

        public static BoardMessageDto ToBoardMessage(BoardGroupCollectionDto groupsData)
        {
            return new BoardMessageDto
            {
                Title = "📋 Seznam všech skupin",
                Description = $"Celkem registrovaných skupin: {groupsData.Groups.Count}",
                Entries = groupsData.Groups.Select(group => new BoardMessageEntryDto
                {
                    Title = group.Name,
                    Emoji = string.IsNullOrWhiteSpace(group.Emoji) ? "🎯" : group.Emoji,
                    Details = BuildDetails(group)
                }).ToList()
            };
        }

        private static string BuildDetails(BoardGroupDto group)
        {
            var details = new List<string>();

            if (!string.IsNullOrWhiteSpace(group.Subtitle))
            {
                details.Add($"**Podtitul:** {group.Subtitle}");
            }

            if (!string.IsNullOrWhiteSpace(group.Contact))
            {
                details.Add($"**Kontakt:** {group.Contact}");
            }

            return details.Count > 0 ? string.Join("\n", details) : string.Empty;
        }
    }
}