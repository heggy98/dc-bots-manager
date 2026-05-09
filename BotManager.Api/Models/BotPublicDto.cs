namespace BotManager.Api.Models
{
    public class BotPublicDto
    {
        public int BotId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
