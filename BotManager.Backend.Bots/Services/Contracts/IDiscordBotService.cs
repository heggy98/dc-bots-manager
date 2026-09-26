using BotManager.Backend.Bots.Models;

namespace BotManager.Backend.Bots.Services.Contracts
{
    /// <summary>
    /// Hosts Discord gateway runtimes; several bots can run side by side.
    /// </summary>
    public interface IDiscordBotService
    {
        /// <summary>
        /// Starts the bot with the given token
        /// </summary>
        Task StartAsync(int botId, string botToken);

        /// <summary>
        /// Stops the given bot
        /// </summary>
        Task StopAsync(int botId);

        /// <summary>
        /// Stops all running bots
        /// </summary>
        Task StopAllAsync();

        /// <summary>
        /// Checks if the bot is currently running
        /// </summary>
        Task<bool> IsRunningAsync(int botId);

        /// <summary>
        /// Gets the bot's current status
        /// </summary>
        string GetStatus(int botId);

        /// <summary>
        /// Refreshes the board message for the configured board channel.
        /// </summary>
        Task<bool> RefreshBoardMessageAsync(int botId, BoardMessageDto boardMessage, int? boardConfigurationId = null);

        /// <summary>
        /// Rebuilds board message reactions from provided emojis only when the message already has reactions.
        /// </summary>
        Task<bool> SyncBoardReactionsIfPresentAsync(int botId, IEnumerable<string> emojis, int? boardConfigurationId = null);
    }
}
