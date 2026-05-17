using Discord;
using BotManager.Backend.Bots.Models;

namespace BotManager.Backend.Bots.Services.Contracts
{
    /// <summary>
    /// Provides the Discord slash command definitions to register with the Discord gateway.
    /// Implement this in the plugin's command service so DiscordBotRuntimeService
    /// does not need to duplicate command schema.
    /// </summary>
    public interface IDiscordCommandProvider
    {
        IReadOnlyCollection<ApplicationCommandProperties> BuildCommands();

        IReadOnlyCollection<DiscordCommandRegistration> GetCommandRegistrations();
    }
}
