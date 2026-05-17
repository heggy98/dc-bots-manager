using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotManager.Backend.Entities.Entities
{
    public class Team
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int TeamId { get; set; }

        public int BoardConfigurationId { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(200)]
        public string LeaderName { get; set; } = string.Empty;

        [MaxLength(200)]
        public string CommanderContact { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Emoji { get; set; } = string.Empty;

        public virtual BoardConfiguration BoardConfiguration { get; set; } = null!;
    }
}
