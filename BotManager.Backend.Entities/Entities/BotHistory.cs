using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotManager.Backend.Entities.Entities
{
    public class BotHistory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int BotId { get; set; }
        public virtual Bot Bot { get; set; } = null!;

        public DateTime StartedAt { get; set; }
        public DateTime? StoppedAt { get; set; }

        /// <summary>Duration in seconds</summary>
        public long? DurationSeconds { get; set; }

        [MaxLength(500)]
        public string? StopReason { get; set; }

        /// <summary>Error message if stopped due to exception</summary>
        [MaxLength(2000)]
        public string? ErrorDetails { get; set; }
    }
}
