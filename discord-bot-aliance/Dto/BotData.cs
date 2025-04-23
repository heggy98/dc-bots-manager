namespace discord_bot_aliance.Dto
{
    public class BotData
    {
        public BotData()
        {
            
        }
        public BotData(ulong teamsMessageId, ulong roleMessageId)
        {
            this.TeamsMessageId = teamsMessageId;
            this.RoleMessageId = roleMessageId;
        }
        public ulong? TeamsMessageId { get; set; }
        public ulong? RoleMessageId { get; set; } // ID zprávy pro výběr role
    }
}
