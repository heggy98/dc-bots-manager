namespace BotManager.Backend.Bots.Models
{
    /// <summary>
    /// Shared command metadata used for app-side command registration.
    /// </summary>
    public sealed class DiscordCommandRegistration
    {
        public required string Name { get; init; }
        public string? SubCommandName { get; init; } // Subcommand name for hierarchical commands (e.g., "add-team" for "/board add-team")
        public required string Description { get; init; }
        public int MinPermissionLevel { get; init; }
        public string? UserHint { get; init; }
        public string? SuccessMessage { get; init; }
        public string? PermissionMessage { get; init; }
        public string? ErrorMessage { get; init; }
    }
}