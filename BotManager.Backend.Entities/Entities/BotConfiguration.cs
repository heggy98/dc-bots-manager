using System.ComponentModel.DataAnnotations;

namespace BotManager.Backend.Entities.Entities
{
    public class BotConfiguration
    {
        [Key]
        public int BotId { get; set; }

        public ulong? BoardChannelId { get; set; }
        public ulong? BoardMessageId { get; set; }

        public virtual Bot Bot { get; set; }
    }
}
