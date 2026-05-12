using BotManager.Backend.Shared.Models;

namespace BotManager.Backend.Bots.Services.Implementations
{
    public static class GroupContractMapper
    {
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