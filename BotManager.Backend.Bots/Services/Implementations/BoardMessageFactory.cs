using BotManager.Backend.Contracts.Models;
using BotManager.Backend.Bots.Models;

namespace BotManager.Backend.Bots.Services.Implementations
{
    public static class BoardMessageFactory
    {
        public static BoardMessageDto FromTeams(BotTeamsDto teamsData)
        {
            return FromGroups(BoardGroupMapper.FromTeams(teamsData));
        }

        public static BoardMessageDto FromGroups(BoardGroupCollectionDto groupsData)
        {
            return BoardGroupMapper.ToBoardMessage(groupsData);
        }
    }
}