using System.ComponentModel.DataAnnotations;

namespace BotManager.Backend.Entities.Entities
{
    public class BotConfiguration
    {
        [Key]
        public int BotId { get; set; }

        public ulong? TeamsMessageId { get; set; }
        public ulong? RoleMessageId { get; set; }
        public ulong? ReactionChannelId { get; set; }

        public virtual Bot Bot { get; set; }
    }
}
