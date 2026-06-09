namespace BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Services
{
    /// <summary>
    /// Encapsulates pure command dispatch decisions for board subcommands.
    /// </summary>
    public static class BoardCommandDispatchPolicy
    {
        /// <summary>
        /// Resolves the subcommand name from slash-command option names.
        /// </summary>
        public static string? ResolveSubcommandName(IEnumerable<string?> optionNames)
        {
            foreach (var optionName in optionNames)
            {
                if (!string.IsNullOrWhiteSpace(optionName))
                {
                    return optionName;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns whether command execution should be denied by admin requirement.
        /// </summary>
        public static bool ShouldDenyForAdmin(bool requiresAdmin, bool isAdministrator)
        {
            return requiresAdmin && !isAdministrator;
        }
    }
}
