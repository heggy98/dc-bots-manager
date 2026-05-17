namespace BotManager.Backend.API.Models
{
    public class GlobalCommandDto
    {
        public string CommandName { get; set; } = string.Empty;
        public string? SubCommandName { get; set; }
        public string? Description { get; set; }
        public int MinimumPermissionLevel { get; set; }
        public bool IsEnabled { get; set; }
        public string? UserHint { get; set; }
        public string? SuccessMessage { get; set; }
        public string? PermissionMessage { get; set; }
        public string? ErrorMessage { get; set; }
        public string? AdminOnlyMessage { get; set; }
        public string? InvalidArgumentsMessage { get; set; }
        public int BotCount { get; set; }
        public bool HasDifferencesAcrossBots { get; set; }
    }

    public class UpdateGlobalCommandDto
    {
        public string? Description { get; set; }
        public int MinimumPermissionLevel { get; set; }
        public bool IsEnabled { get; set; }
        public string? UserHint { get; set; }
        public string? SuccessMessage { get; set; }
        public string? PermissionMessage { get; set; }
        public string? ErrorMessage { get; set; }
        public string? AdminOnlyMessage { get; set; }
        public string? InvalidArgumentsMessage { get; set; }
    }
}
