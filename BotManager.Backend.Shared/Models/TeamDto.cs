using System.Text.Json.Serialization;

namespace BotManager.Backend.Shared.Models
{
    public class TeamDto
    {
        public int? TeamId { get; set; }

        /// <summary>
        /// Discord role bound to the team. Server-side only (not exposed to API clients,
        /// ulong ids would also lose precision in JavaScript).
        /// </summary>
        [JsonIgnore]
        public ulong? RoleId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string LeaderName { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public string Emoji { get; set; } = string.Empty;
    }

    public class BotTeamsDto
    {
        public List<TeamDto> Teams { get; set; } = new();
    }
}

