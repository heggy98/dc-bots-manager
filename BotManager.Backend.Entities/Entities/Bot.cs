using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotManager.Backend.Entities.Entities
{
    public enum BotStatus
    {
        Offline = 0,
        Online = 1,
        Working = 2,
        Reconnecting = 3,
        Connecting = 4,
        Disconnecting = 5
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

        [Required]
        [MaxLength(200)]
        public string OwnerUserId { get; set; } = string.Empty;

        public bool IsPublic { get; set; } = false;

        public BotStatus Status { get; set; } = BotStatus.Offline;

        public DateTime? LastStartedAt { get; set; }
        public DateTime? LastStoppedAt { get; set; }

        // Navigation properties
        public virtual BotConfiguration? Configuration { get; set; }
        public virtual ICollection<BoardConfiguration> BoardConfigurations { get; set; } = new List<BoardConfiguration>();
        public virtual ICollection<BotRunHistory> Histories { get; set; } = new List<BotRunHistory>();
        public virtual ICollection<BotCommand> Commands { get; set; } = new List<BotCommand>();
    }
}
