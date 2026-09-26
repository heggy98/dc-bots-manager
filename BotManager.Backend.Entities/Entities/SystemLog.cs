using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotManager.Backend.Entities.Entities
{
    public class SystemLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [MaxLength(50)]
        public string Level { get; set; } = string.Empty;

        /// <summary>Logger source context; null for events without one.</summary>
        [MaxLength(500)]
        public string? Category { get; set; }

        /// <summary>Bot the log event belongs to (filled from the structured "BotId" log property).</summary>
        public int? BotId { get; set; }

        [MaxLength(4000)]
        public string Message { get; set; } = string.Empty;

        [MaxLength(4000)]
        public string? Exception { get; set; }
    }
}
