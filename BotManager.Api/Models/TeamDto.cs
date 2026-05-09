namespace BotManager.Api.Models
{
    public class TeamDto
    {
        public int? TeamId { get; set; }
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

