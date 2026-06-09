using Discord;
using Discord.WebSocket;
using BotManager.Backend.Shared.Models;

namespace BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Services
{
    public interface IBoardCommandService
    {
        IReadOnlyCollection<ApplicationCommandProperties> BuildCommands();

        Task<bool> DispatchAsync(
            SocketSlashCommand command,
            SocketGuild guild,
            SocketTextChannel channel,
            SocketGuildUser user,
            IPluginContext context);

        Task HandleReactionAddedAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            ISocketMessageChannel channel,
            SocketReaction reaction,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context);

        Task HandleReactionRemovedAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            ISocketMessageChannel channel,
            SocketReaction reaction,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context);

        Task HandleButtonInteractionAsync(
            SocketMessageComponent component,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context);

        Task HandleBoardActionButtonAsync(
            SocketMessageComponent component,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context);

        Task HandleBoardModalAsync(
            SocketModal modal,
            SocketGuild guild,
            SocketGuildUser user,
            IPluginContext context);
    }
}
