using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotManager.Backend.Entities.Entities
{
    public class BoardConfiguration
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int BoardConfigurationId { get; set; }

        public int BotId { get; set; }

        [MaxLength(50)]
        public string BoardType { get; set; } = "teams";

        public ulong? GuildId { get; set; }
        public ulong? BoardChannelId { get; set; }
        public ulong? BoardMessageId { get; set; }

        [MaxLength(250)]
        public string? BoardTitle { get; set; }

        [MaxLength(500)]
        public string? BoardDescriptionTemplate { get; set; }

        [MaxLength(100)]
        public string? SubtitleLabel { get; set; }

        [MaxLength(100)]
        public string? ContactLabel { get; set; }

        public virtual Bot Bot { get; set; } = null!;
        public virtual ICollection<Team> Teams { get; set; } = new List<Team>();
    }
}