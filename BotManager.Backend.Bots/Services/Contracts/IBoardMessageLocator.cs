using Discord;
using Discord.WebSocket;

namespace BotManager.Backend.Bots.Services.Contracts
{
    /// <summary>
    /// Locates board marker messages in Discord text channels.
    /// </summary>
    public interface IBoardMessageLocator
    {
        Task<IUserMessage?> FindBoardMessageAsync(SocketTextChannel channel, string marker, int historyLimit = 100);
    }
}