namespace BotManager.Backend.Bots.Models
{
    public class BoardMessageDto
    {
        public string Title { get; set; } = "📋 Seznam položek";
        public string? Description { get; set; }
        public List<BoardMessageEntryDto> Entries { get; set; } = new();
    }

    public class BoardMessageEntryDto
    {
        public string Title { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string? Emoji { get; set; }
    }
}