using BotManager.Backend.Shared.Models;
using BotManager.Backend.Bots.Models;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Maps between team data, board group data, and board message DTOs.
    /// </summary>
    public static class BoardGroupMapper
    {
        /// <summary>
        /// Maps team DTOs to board group DTOs.
        /// </summary>
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

        /// <summary>
        /// Maps board group DTOs to team DTOs.
        /// </summary>
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

        /// <summary>
        /// Builds a board message DTO from board group data.
        /// </summary>
        public static BoardMessageDto ToBoardMessage(BoardGroupCollectionDto groupsData, BotConfigurationDto? boardConfig = null)
        {
            var title = boardConfig?.BoardTitle ?? string.Empty;
            var descriptionTemplate = boardConfig?.BoardDescriptionTemplate ?? string.Empty;

            return new BoardMessageDto
            {
                Title = title,
                Description = BuildDescription(descriptionTemplate, groupsData.Groups.Count),
                Entries = groupsData.Groups.Select(group => new BoardMessageEntryDto
                {
                    Title = group.Name,
                    Emoji = string.IsNullOrWhiteSpace(group.Emoji) ? "🎯" : group.Emoji,
                    Details = BuildDetails(group, boardConfig)
                }).ToList()
            };
        }

        /// <summary>
        /// Builds display details text for a single board group row.
        /// </summary>
        private static string BuildDetails(BoardGroupDto group, BotConfigurationDto? boardConfig)
        {
            var details = new List<string>();

            var subtitleLabel = boardConfig?.SubtitleLabel;
            var contactLabel = boardConfig?.ContactLabel;

            if (!string.IsNullOrWhiteSpace(group.Subtitle))
            {
                details.Add(FormatDetailLine(subtitleLabel, group.Subtitle));
            }

            if (!string.IsNullOrWhiteSpace(group.Contact))
            {
                details.Add(FormatDetailLine(contactLabel, group.Contact));
            }

            return details.Count > 0 ? string.Join("\n", details) : string.Empty;
        }

        /// <summary>
        /// Formats detail lines with optional labels.
        /// </summary>
        private static string FormatDetailLine(string? label, string value)
        {
            return string.IsNullOrWhiteSpace(label)
                ? value
                : $"**{label}:** {value}";
        }

        /// <summary>
        /// Builds board description text from a template with supported tokens.
        /// </summary>
        private static string BuildDescription(string template, int groupCount)
        {
            return template
                .Replace("{count}", groupCount.ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("{groupCount}", groupCount.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}