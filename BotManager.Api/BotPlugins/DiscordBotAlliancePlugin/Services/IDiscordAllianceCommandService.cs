using Discord;
using Discord.WebSocket;

namespace BotManager.Api.BotPlugins.DiscordBotAlliancePlugin.Services
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
    }
}
