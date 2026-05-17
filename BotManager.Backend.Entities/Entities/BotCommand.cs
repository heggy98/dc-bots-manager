using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotManager.Backend.Entities.Entities
{
    [Table("BotCommands")]
    public class BotCommand
    {
        [Key]
        public int CommandId { get; set; }

        [Required]
        [ForeignKey("Bot")]
        public int BotId { get; set; }

        [Required]
        [MaxLength(100)]
        public string CommandName { get; set; } = null!;

        [MaxLength(100)]
        public string? SubCommandName { get; set; } // Subcommand name (e.g., "add-team" for "/board add-team")

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        public int MinimumPermissionLevel { get; set; } // 0 = Everyone, 1 = Moderator, 2 = Administrator

        [Required]
        public bool IsEnabled { get; set; } = true;

        [MaxLength(2000)]
        public string? CommandParameters { get; set; } // JSON-serialized parameters

        // Localized strings for command responses
        [MaxLength(500)]
        public string? UserHint { get; set; } // Hint displayed when user hovers over option

        [MaxLength(1000)]
        public string? SuccessMessage { get; set; } // Success feedback message

        [MaxLength(500)]
        public string? PermissionMessage { get; set; } // Permission denied message

        [MaxLength(500)]
        public string? ErrorMessage { get; set; } // Generic error message

        [MaxLength(500)]
        public string? AdminOnlyMessage { get; set; } // Message when non-admin tries admin command

        [MaxLength(500)]
        public string? InvalidArgumentsMessage { get; set; } // Message when arguments are invalid

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public virtual Bot? Bot { get; set; }
        public virtual ICollection<CommandUsageLog> UsageLogs { get; set; } = new List<CommandUsageLog>();
    }
}
