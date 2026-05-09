namespace BotManager.Api.Models
{
    public class AdminBotDto
    {
        public int BotId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string BotToken { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Requests24h { get; set; }
        public int Errors24h { get; set; }
        public DateTime? LastStartedAt { get; set; }
        public DateTime? LastStoppedAt { get; set; }
    }
}
