using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Models;
using BotManager.Backend.Shared.Models;
using BotManager.Backend.Shared.Services;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Maps between board group DTOs and shared group storage DTOs.
    /// </summary>
    public class TeamBoardGroupDataService : IGroupBoardDataService
    {
        private readonly IGroupDataService _groupDataService;

        /// <summary>
        /// Creates a new board group mapping service.
        /// </summary>
        public TeamBoardGroupDataService(IGroupDataService groupDataService)
        {
            _groupDataService = groupDataService;
        }

        /// <summary>
        /// Loads board groups for a bot from shared group data storage.
        /// </summary>
        public async Task<BoardGroupCollectionDto> GetAsync(int botId, int? boardConfigurationId = null)
        {
            var groupsData = await _groupDataService.GetAsync(botId, boardConfigurationId);
            return new BoardGroupCollectionDto
            {
                Groups = groupsData.Groups.Select(group => new BoardGroupDto
                {
                    Id = group.GroupId,
                    Name = group.Name,
                    Subtitle = group.OwnerName,
                    Contact = group.Contact,
                    Emoji = group.Icon
                }).ToList()
            };
        }

        /// <summary>
        /// Saves board groups by converting them to shared group data DTOs.
        /// </summary>
        public async Task SaveAsync(int botId, BoardGroupCollectionDto data, int? boardConfigurationId = null)
        {
            var groupsData = new BotGroupsDto
            {
                Groups = data.Groups.Select(group => new GroupDto
                {
                    GroupId = group.Id,
                    Name = group.Name,
                    OwnerName = group.Subtitle ?? string.Empty,
                    Contact = group.Contact ?? string.Empty,
                    Icon = group.Emoji ?? string.Empty
                }).ToList()
            };

            await _groupDataService.SaveAsync(botId, groupsData, boardConfigurationId);
        }
    }
}