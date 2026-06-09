using BotManager.Backend.Shared.Models;

namespace BotManager.Backend.API.Models
{
    public class AdminBotDetailDto
    {
        public int BotId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string BotToken { get; set; } = string.Empty;
        public string OwnerUserId { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public string? DiscordBotName { get; set; }
        public string? DiscordBotAvatarUrl { get; set; }
        public int? ServerCount { get; set; }
        public List<string> Guilds { get; set; } = new();
        public string Status { get; set; } = string.Empty;
        public int Requests24h { get; set; }
        public int Errors24h { get; set; }
        public DateTime? LastStartedAt { get; set; }
        public DateTime? LastStoppedAt { get; set; }
        public bool IsTokenAuthorized { get; set; }

        public BotConfigurationDto Configuration { get; set; } = new();
        public List<BotLogDto> Logs { get; set; } = new();
        public List<BotHistoryDto> Histories { get; set; } = new();
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
