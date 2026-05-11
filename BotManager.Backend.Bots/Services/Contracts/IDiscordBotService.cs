using BotManager.Backend.Bots.Models;

namespace BotManager.Backend.Bots.Services.Contracts
{
    public interface IDiscordBotService
    {
        /// <summary>
        /// Starts the bot with the given token
        /// </summary>
        Task StartAsync(int botId, string botToken);

        /// <summary>
        /// Stops the running bot
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// Checks if the bot is currently running
        /// </summary>
        Task<bool> IsRunningAsync();

        /// <summary>
        /// Gets the bot's current status
        /// </summary>
        string GetStatus();

        /// <summary>
        /// Refreshes the board message for the configured board channel.
        /// </summary>
        Task<bool> RefreshBoardMessageAsync(int botId, BoardMessageDto boardMessage);
    }
}
