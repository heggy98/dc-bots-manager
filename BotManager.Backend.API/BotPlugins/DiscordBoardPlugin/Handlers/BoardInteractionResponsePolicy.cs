namespace BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Handlers
{
    /// <summary>
    /// Encapsulates pure decision rules for interaction response behavior.
    /// </summary>
    public static class BoardInteractionResponsePolicy
    {
        public static bool ShouldUseFollowup(bool hasResponded, long interactionAgeMs)
        {
            // Near or past the 3-second interaction deadline, initial responses are likely to fail.
            return hasResponded || interactionAgeMs >= 2500;
        }

        public static bool ShouldAttemptLateDefer(bool hasResponded)
        {
            return !hasResponded;
        }

        public static bool IsAlreadyAcknowledgedError(int? discordCode)
        {
            return discordCode == 40060;
        }

        public static bool IsExpiredInteractionError(int? discordCode)
        {
            return discordCode == 10015 || discordCode == 10062;
        }
    }
}
