namespace BotManager.Backend.API.Models
{
    /// <summary>
    /// Portable bot configuration document produced by export and consumed by import.
    /// Never contains the bot token, Discord role ids or board message ids.
    /// </summary>
    public class BotConfigExportDocument
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; }
        public DateTime ExportedAt { get; set; }
        public ExportedBotSettings? Bot { get; set; }
        public List<ExportedBoard>? Boards { get; set; }
        public List<ExportedCommand>? Commands { get; set; }
    }

    public class ExportedBotSettings
    {
        public string? Name { get; set; }
        public bool IsPublic { get; set; }
        public bool AutoStart { get; set; }
    }

    public class ExportedBoard
    {
        public string? BoardType { get; set; }

        /// <summary>Optional on import (skipped when the import is told not to apply Discord ids).</summary>
        [System.Text.Json.Serialization.JsonConverter(typeof(BotManager.Backend.API.Controllers.FlexibleStringConverter))]
        public string? GuildId { get; set; }

        /// <summary>Optional on import (skipped when the import is told not to apply Discord ids).</summary>
        [System.Text.Json.Serialization.JsonConverter(typeof(BotManager.Backend.API.Controllers.FlexibleStringConverter))]
        public string? BoardChannelId { get; set; }

        public string? BoardTitle { get; set; }
        public string? BoardDescriptionTemplate { get; set; }
        public string? SubtitleLabel { get; set; }
        public string? ContactLabel { get; set; }
        public bool IsActive { get; set; }
        public List<ExportedTeam>? Teams { get; set; }
    }

    public class ExportedTeam
    {
        public string? Name { get; set; }
        public string? LeaderName { get; set; }
        public string? Contact { get; set; }
        public string? Emoji { get; set; }
    }

    public class ExportedCommand
    {
        public string? CommandName { get; set; }
        public string? SubCommandName { get; set; }
        public string? Description { get; set; }
        public int MinimumPermissionLevel { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string? UserHint { get; set; }
        public string? SuccessMessage { get; set; }
        public string? PermissionMessage { get; set; }
        public string? ErrorMessage { get; set; }
        public string? AdminOnlyMessage { get; set; }
        public string? InvalidArgumentsMessage { get; set; }
    }

    /// <summary>
    /// Result of an import (or of a dry run when <see cref="DryRun"/> is set).
    /// </summary>
    public class BotConfigImportSummary
    {
        public bool DryRun { get; set; }
        public string Mode { get; set; } = "replace";
        public bool AppliedDiscordIds { get; set; }
        public string? BotName { get; set; }
        public int BoardsRemoved { get; set; }
        public int BoardsImported { get; set; }
        public int TeamsImported { get; set; }
        public int CommandsUpdated { get; set; }
        public int CommandsCreated { get; set; }
        public List<string> Warnings { get; set; } = [];
    }
}
