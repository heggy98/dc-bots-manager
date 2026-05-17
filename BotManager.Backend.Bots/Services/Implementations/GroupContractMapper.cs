using BotManager.Backend.Shared.Models;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Maps between team DTOs and shared group DTO contracts.
    /// </summary>
    public static class GroupContractMapper
    {
        /// <summary>
        /// Maps team DTOs to group DTOs.
        /// </summary>
        public static BotGroupsDto FromTeams(BotTeamsDto teamsData)
        {
            return new BotGroupsDto
            {
                Groups = teamsData.Teams.Select(team => new GroupDto
                {
                    GroupId = team.TeamId,
                    Name = team.Name,
                    OwnerName = team.LeaderName,
                    Contact = team.Contact,
                    Icon = team.Emoji
                }).ToList()
            };
        }

        /// <summary>
        /// Maps group DTOs back to team DTOs.
        /// </summary>
        public static BotTeamsDto ToTeams(BotGroupsDto groupsData)
        {
            return new BotTeamsDto
            {
                Teams = groupsData.Groups.Select(group => new TeamDto
                {
                    TeamId = group.GroupId,
                    Name = group.Name,
                    LeaderName = group.OwnerName,
                    Contact = group.Contact,
                    Emoji = group.Icon
                }).ToList()
            };
        }
    }
}