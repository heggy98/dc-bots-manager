using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BotManager.Backend.API.Hubs
{
    /// <summary>
    /// SignalR hub for real-time bot event delivery.
    /// Clients join a per-bot group to receive targeted notifications.
    /// </summary>
    [Authorize]
    public class BotEventsHub : Hub
    {
        /// <summary>
        /// Subscribes the current connection to events for the given bot.
        /// </summary>
        public async Task JoinBotGroup(int botId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(botId));
        }

        /// <summary>
        /// Unsubscribes the current connection from events for the given bot.
        /// </summary>
        public async Task LeaveBotGroup(int botId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(botId));
        }

        internal static string GroupName(int botId) => $"bot_{botId}";
    }
}
