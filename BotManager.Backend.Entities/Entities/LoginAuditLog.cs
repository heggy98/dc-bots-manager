using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotManager.Backend.Entities.Entities
{
    public class LoginAuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [MaxLength(200)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(50)]
        public string IpAddress { get; set; } = string.Empty;

        public bool Success { get; set; }

        [MaxLength(200)]
        public string? FailReason { get; set; }

        /// <summary>True if this entry marks a bruteforce lockout</summary>
        public bool IsBruteforceBlock { get; set; }
    }
}
