using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Models;
using BotManager.Backend.Contracts.Models;
using BotManager.Backend.Contracts.Services;

namespace BotManager.Api.Services
{
    public class TeamBoardGroupDataService : IGroupBoardDataService
    {
        private readonly IGroupDataService _groupDataService;

        public TeamBoardGroupDataService(IGroupDataService groupDataService)
        {
            _groupDataService = groupDataService;
        }

        public async Task<BoardGroupCollectionDto> GetAsync(int botId)
        {
            var groupsData = await _groupDataService.GetAsync(botId);
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

        public async Task SaveAsync(int botId, BoardGroupCollectionDto data)
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

            await _groupDataService.SaveAsync(botId, groupsData);
        }
    }
}