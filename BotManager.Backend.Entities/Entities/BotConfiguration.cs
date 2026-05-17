using System.ComponentModel.DataAnnotations;

namespace BotManager.Backend.Entities.Entities
{
    public class BotConfiguration
    {
        [Key]
        public int BotId { get; set; }

        public int? ActiveBoardConfigurationId { get; set; }

        public virtual Bot Bot { get; set; } = null!;
        public virtual BoardConfiguration? ActiveBoardConfiguration { get; set; }
    }
}
