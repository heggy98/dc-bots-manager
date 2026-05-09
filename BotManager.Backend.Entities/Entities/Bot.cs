using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotManager.Backend.Entities.Entities
{
    public enum BotStatus
    {
        Offline = 0,
        Online = 1,
        Working = 2
    }

    public class Bot
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int BotId { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(1000)]
        public string BotToken { get; set; } = string.Empty;

        public BotStatus Status { get; set; } = BotStatus.Offline;

        public DateTime? LastStartedAt { get; set; }
        public DateTime? LastStoppedAt { get; set; }

        // Navigation properties
        public virtual BotConfiguration? Configuration { get; set; }
        public virtual ICollection<BotHistory> Histories { get; set; } = new List<BotHistory>();
        public virtual ICollection<BotCommand> Commands { get; set; } = new List<BotCommand>();
    }
}
