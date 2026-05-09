namespace BotManager.Backend.Services.Interfaces
{
    public interface IDiscordBotService
    {
        /// <summary>
        /// Starts the bot with the given token
        /// </summary>
        Task StartAsync(string botToken);

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
    }
}
