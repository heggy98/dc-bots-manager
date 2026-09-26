using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotManager.Backend.Entities.Entities
{
    /// <summary>
    /// Records a state-changing action performed by an administrator (who did what, to what, from where).
    /// Must never contain secrets (bot tokens, 2FA codes, passwords).
    /// </summary>
    public class AdminAuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>UTC time of the action.</summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [MaxLength(200)]
        public string ActorEmail { get; set; } = string.Empty;

        /// <summary>Dotted action name, e.g. "bot.start" or "auth.2fa.enable".</summary>
        [MaxLength(100)]
        public string Action { get; set; } = string.Empty;

        /// <summary>Kind of the affected object, e.g. "bot", "board", "config".</summary>
        [MaxLength(50)]
        public string? TargetType { get; set; }

        /// <summary>Identifier of the affected object.</summary>
        [MaxLength(200)]
        public string? TargetId { get; set; }

        /// <summary>Short JSON/text with non-secret details.</summary>
        [MaxLength(2000)]
        public string? Details { get; set; }

        [MaxLength(50)]
        public string? IpAddress { get; set; }
    }
}
