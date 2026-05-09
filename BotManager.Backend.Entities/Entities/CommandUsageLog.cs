using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotManager.Backend.Entities.Entities
{
    [Table("CommandUsageLogs")]
    public class CommandUsageLog
    {
        [Key]
        public int UsageId { get; set; }

        [Required]
        [ForeignKey("BotCommand")]
        public int CommandId { get; set; }

        [Required]
        public ulong UserId { get; set; } // Discord user ID

        [MaxLength(100)]
        public string? UserName { get; set; } // Discord username at time of execution

        [Required]
        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(500)]
        public string? ErrorMessage { get; set; } // If command failed, capture the error

        public bool IsSuccess { get; set; } = true;

        // Navigation
        public virtual BotCommand? BotCommand { get; set; }
    }
}
