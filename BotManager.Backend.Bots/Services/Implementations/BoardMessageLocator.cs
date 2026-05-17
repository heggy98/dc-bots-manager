using BotManager.Backend.Bots.Services.Contracts;
using Discord;
using Discord.WebSocket;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Default board marker message locator based on recent channel history.
    /// </summary>
    public class BoardMessageLocator : IBoardMessageLocator
    {
        public async Task<IUserMessage?> FindBoardMessageAsync(SocketTextChannel channel, string marker, int historyLimit = 100)
        {
            var messages = await channel.GetMessagesAsync(limit: historyLimit).FlattenAsync();
            return messages
                .OfType<IUserMessage>()
                .FirstOrDefault(m => string.Equals(m.Content, marker, StringComparison.Ordinal));
        }
    }
}