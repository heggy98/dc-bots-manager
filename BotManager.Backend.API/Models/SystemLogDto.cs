namespace BotManager.Backend.API.Models
{
    public class SystemLogDto
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
