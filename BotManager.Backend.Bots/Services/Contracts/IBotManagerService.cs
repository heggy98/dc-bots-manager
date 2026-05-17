namespace BotManager.Backend.Bots.Services.Contracts
{
    /// <summary>
    /// Defines process-level bot runtime start/stop operations.
    /// </summary>
    public interface IBotManagerService
    {
        /// <summary>
        /// Starts the managed bot process.
        /// </summary>
        void StartBot();

        /// <summary>
        /// Stops the managed bot process.
        /// </summary>
        void StopBot();
    }
}
