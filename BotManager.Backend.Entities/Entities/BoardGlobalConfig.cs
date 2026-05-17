using System.ComponentModel.DataAnnotations;

namespace BotManager.Backend.Entities.Entities
{
    public class BoardGlobalConfig
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(250)]
        public string? DefaultBoardTitle { get; set; }

        [MaxLength(500)]
        public string? DefaultBoardDescription { get; set; }

        [MaxLength(100)]
        public string? DefaultBoardDetailSubtitleLabel { get; set; }

        [MaxLength(100)]
        public string? DefaultBoardDetailContactLabel { get; set; }
    }
}