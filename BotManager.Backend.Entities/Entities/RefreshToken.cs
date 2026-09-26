using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotManager.Backend.Entities.Entities
{
    /// <summary>
    /// Opaque refresh token issued at login. Only a SHA-256 hash of the token is stored.
    /// Tokens are rotated on every use; presenting an already rotated token revokes the whole family.
    /// </summary>
    public class RefreshToken
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>Hex SHA-256 of the raw token.</summary>
        [Required]
        [MaxLength(64)]
        public string TokenHash { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string Email { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; }

        public DateTime? RevokedAt { get; set; }

        /// <summary>Hash of the token that replaced this one during rotation.</summary>
        [MaxLength(64)]
        public string? ReplacedByTokenHash { get; set; }

        [MaxLength(50)]
        public string? CreatedByIp { get; set; }
    }
}
