using Discord;
using Discord.WebSocket;

namespace BotManager.Backend.API.BotPlugins.DiscordBotAlliancePlugin.Services
{
    public interface IDiscordAllianceCommandService
    {
        IReadOnlyCollection<ApplicationCommandProperties> BuildCommands();

        Task<bool> DispatchAsync(
            SocketSlashCommand command,
            SocketGuild guild,
            SocketTextChannel channel,
            SocketGuildUser user,
            PluginContext context);

        Task HandleReactionAddedAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            ISocketMessageChannel channel,
            SocketReaction reaction,
            SocketGuild guild,
            SocketGuildUser user,
            PluginContext context);

        Task HandleReactionRemovedAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            ISocketMessageChannel channel,
            SocketReaction reaction,
            SocketGuild guild,
            SocketGuildUser user,
            PluginContext context);
    }
}
