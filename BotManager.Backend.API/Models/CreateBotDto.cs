namespace BotManager.Backend.API.Models
{
    public class CreateBotDto
    {
        public string Name { get; set; } = string.Empty;
        public string BotToken { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
    }
}
