namespace BotManager.Backend.Shared.Models
{
    public class GroupDto
    {
        public int? GroupId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
    }

    public class BotGroupsDto
    {
        public List<GroupDto> Groups { get; set; } = new();
    }
}