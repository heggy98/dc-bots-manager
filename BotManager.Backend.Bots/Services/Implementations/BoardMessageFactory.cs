using BotManager.Backend.Shared.Models;
using BotManager.Backend.Bots.Models;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Provides convenience methods for creating board messages from team or group data.
    /// </summary>
    public static class BoardMessageFactory
    {
        /// <summary>
        /// Creates a board message DTO from team DTOs.
        /// </summary>
        public static BoardMessageDto FromTeams(BotTeamsDto teamsData, BotConfigurationDto? boardConfig = null)
        {
            return FromGroups(BoardGroupMapper.FromTeams(teamsData), boardConfig);
        }

        /// <summary>
        /// Creates a board message DTO from board group DTOs.
        /// </summary>
        public static BoardMessageDto FromGroups(BoardGroupCollectionDto groupsData, BotConfigurationDto? boardConfig = null)
        {
            return BoardGroupMapper.ToBoardMessage(groupsData, boardConfig);
        }
    }
}