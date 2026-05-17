namespace BotManager.Backend.Shared.Models
{
    public class BotConfigurationDto
    {
        public int? ActiveBoardConfigurationId { get; set; }
        public string? BoardType { get; set; }
        public string? BoardChannelId { get; set; }
        public string? BoardMessageId { get; set; }
        public string? BoardTitle { get; set; }
        public string? BoardDescriptionTemplate { get; set; }
        public string? SubtitleLabel { get; set; }
        public string? ContactLabel { get; set; }
    }
}
