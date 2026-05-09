namespace BotManager.Api.Models
{
    public class CreateBotDto
    {
        public string Name { get; set; } = string.Empty;
        public string BotToken { get; set; } = string.Empty;
    }
}
