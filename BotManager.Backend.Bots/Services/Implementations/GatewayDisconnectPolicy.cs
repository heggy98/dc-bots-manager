using BotManager.Backend.Entities.Entities;
using Discord;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Centralized policy that decides whether a disconnect should transition the bot to Offline.
    /// </summary>
    public static class GatewayDisconnectPolicy
    {
        public static bool ShouldMarkOffline(
            int disconnectGeneration,
            int currentGeneration,
            ConnectionState connectionState,
            BotStatus? persistedStatus)
        {
            // A newer connection/disconnect event superseded this one.
            if (disconnectGeneration != currentGeneration)
            {
                return false;
            }

            // Gateway is already connected again.
            if (connectionState == ConnectionState.Connected)
            {
                return false;
            }

            // DB already reflects an online state restored by reconnect handling.
            if (persistedStatus == BotStatus.Online)
            {
                return false;
            }

            return true;
        }
    }
}