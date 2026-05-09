namespace BotManager.Api.Models
{
    public class AdminBotDetailDto
    {
        public int BotId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string BotToken { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Requests24h { get; set; }
        public int Errors24h { get; set; }
        public DateTime? LastStartedAt { get; set; }
        public DateTime? LastStoppedAt { get; set; }

        public BotConfigurationDto Configuration { get; set; } = new();
        public List<BotLogDto> Logs { get; set; } = new();
        public List<BotHistoryDto> Histories { get; set; } = new();
    }

    public class BotConfigurationDto
    {
        public ulong? TeamsMessageId { get; set; }
        public ulong? RoleMessageId { get; set; }
        public ulong? ReactionChannelId { get; set; }
    }

    public class BotLogDto
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class BotHistoryDto
    {
        public int Id { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? StoppedAt { get; set; }
        public long? DurationSeconds { get; set; }
        public string? StopReason { get; set; }
        public string? ErrorDetails { get; set; }
    }
}
