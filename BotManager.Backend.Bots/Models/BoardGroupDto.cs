namespace BotManager.Backend.Bots.Models
{
    public class BoardGroupDto
    {
        public int? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string? Contact { get; set; }
        public string? Emoji { get; set; }
    }

    public class BoardGroupCollectionDto
    {
        public List<BoardGroupDto> Groups { get; set; } = new();
    }
}