using BotManager.Backend.API.Models;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Exports a bot's configuration to a portable JSON document and imports such a document back
    /// (mode "replace"). The bot token, Discord role ids and board message ids are never exported.
    /// </summary>
    public class BotConfigTransferService
    {
        public const string DefaultTeamEmoji = "🎯";

        private readonly BotManagerDbContext _db;
        private readonly Func<DateTime> _utcNow;

        public BotConfigTransferService(BotManagerDbContext db, Func<DateTime>? utcNow = null)
        {
            _db = db;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// Builds the export document for a bot, or null when the bot does not exist.
        /// </summary>
        public async Task<BotConfigExportDocument?> ExportAsync(int botId, CancellationToken ct = default)
        {
            var bot = await _db.Bots.AsNoTracking()
                .Where(b => b.BotId == botId)
                .Select(b => new { b.Name, b.IsPublic, b.AutoStart })
                .FirstOrDefaultAsync(ct);
            if (bot == null)
            {
                return null;
            }

            var activeBoardId = await _db.BotConfigurations.AsNoTracking()
                .Where(c => c.BotId == botId)
                .Select(c => c.ActiveBoardConfigurationId)
                .FirstOrDefaultAsync(ct);

            var boards = await _db.BoardConfigurations.AsNoTracking()
                .Where(c => c.BotId == botId)
                .OrderBy(c => c.BoardConfigurationId)
                .Select(c => new
                {
                    c.BoardConfigurationId,
                    c.BoardType,
                    c.GuildId,
                    c.BoardChannelId,
                    c.BoardTitle,
                    c.BoardDescriptionTemplate,
                    c.SubtitleLabel,
                    c.ContactLabel,
                    Teams = c.Teams
                        .OrderBy(t => t.TeamId)
                        .Select(t => new ExportedTeam
                        {
                            Name = t.Name,
                            LeaderName = t.LeaderName,
                            Contact = t.CommanderContact,
                            Emoji = t.Emoji
                        })
                        .ToList()
                })
                .ToListAsync(ct);

            var commands = await _db.BotCommands.AsNoTracking()
                .Where(c => c.BotId == botId)
                .OrderBy(c => c.CommandName).ThenBy(c => c.SubCommandName)
                .Select(c => new ExportedCommand
                {
                    CommandName = c.CommandName,
                    SubCommandName = c.SubCommandName,
                    Description = c.Description,
                    MinimumPermissionLevel = c.MinimumPermissionLevel,
                    IsEnabled = c.IsEnabled,
                    UserHint = c.UserHint,
                    SuccessMessage = c.SuccessMessage,
                    PermissionMessage = c.PermissionMessage,
                    ErrorMessage = c.ErrorMessage,
                    AdminOnlyMessage = c.AdminOnlyMessage,
                    InvalidArgumentsMessage = c.InvalidArgumentsMessage
                })
                .ToListAsync(ct);

            return new BotConfigExportDocument
            {
                SchemaVersion = BotConfigExportDocument.CurrentSchemaVersion,
                ExportedAt = _utcNow(),
                Bot = new ExportedBotSettings { Name = bot.Name, IsPublic = bot.IsPublic, AutoStart = bot.AutoStart },
                Boards = boards
                    .Select(b => new ExportedBoard
                    {
                        BoardType = b.BoardType,
                        GuildId = b.GuildId?.ToString(),
                        BoardChannelId = b.BoardChannelId?.ToString(),
                        BoardTitle = b.BoardTitle,
                        BoardDescriptionTemplate = b.BoardDescriptionTemplate,
                        SubtitleLabel = b.SubtitleLabel,
                        ContactLabel = b.ContactLabel,
                        IsActive = activeBoardId == b.BoardConfigurationId,
                        Teams = b.Teams
                    })
                    .ToList(),
                Commands = commands
            };
        }

        /// <summary>
        /// Validates and imports a document in "replace" mode inside one DB transaction: bot settings are
        /// overwritten, all boards (and their teams) are replaced, and commands are upserted by
        /// (commandName, subCommandName) - existing commands missing from the document are kept, because
        /// deleting them would delete their usage history. A null <c>bot</c>/<c>boards</c>/<c>commands</c>
        /// section leaves that part untouched. With <paramref name="dryRun"/> nothing is written.
        /// </summary>
        /// <exception cref="KeyNotFoundException">The bot does not exist.</exception>
        /// <exception cref="BotConfigImportValidationException">The document is invalid.</exception>
        public async Task<BotConfigImportSummary> ImportAsync(
            int botId,
            BotConfigExportDocument? document,
            bool applyDiscordIds = true,
            bool dryRun = false,
            CancellationToken ct = default)
        {
            var errors = BotConfigImportValidator.Validate(document);
            if (errors.Count > 0)
            {
                throw new BotConfigImportValidationException(errors);
            }

            var doc = document!;
            var bot = await _db.Bots.FirstOrDefaultAsync(b => b.BotId == botId, ct)
                ?? throw new KeyNotFoundException($"Bot {botId} not found");

            var summary = new BotConfigImportSummary
            {
                DryRun = dryRun,
                AppliedDiscordIds = applyDiscordIds,
                BotName = doc.Bot?.Name?.Trim() ?? bot.Name
            };

            var existingBoards = doc.Boards == null
                ? []
                : await _db.BoardConfigurations
                    .Where(c => c.BotId == botId)
                    .Include(c => c.Teams)
                    .ToListAsync(ct);
            var existingCommands = doc.Commands == null
                ? []
                : await _db.BotCommands.Where(c => c.BotId == botId).ToListAsync(ct);
            var commandsByKey = existingCommands
                .GroupBy(c => CommandKey(c.CommandName, c.SubCommandName))
                .ToDictionary(g => g.Key, g => g.First());

            if (doc.Boards != null)
            {
                summary.BoardsRemoved = existingBoards.Count;
                summary.BoardsImported = doc.Boards.Count;
                summary.TeamsImported = doc.Boards.Sum(b => b.Teams?.Count ?? 0);
                if (existingBoards.Any(b => b.Teams.Any(t => t.RoleId.HasValue)))
                {
                    summary.Warnings.Add("Existing teams are replaced; their Discord role bindings are dropped.");
                }
                if (existingBoards.Any(b => b.BoardMessageId.HasValue))
                {
                    summary.Warnings.Add("Board message ids are not imported; the bot posts new board messages.");
                }
            }

            if (doc.Commands != null)
            {
                foreach (var command in doc.Commands)
                {
                    if (commandsByKey.ContainsKey(CommandKey(command.CommandName!.Trim(), NormalizeNullable(command.SubCommandName))))
                        summary.CommandsUpdated++;
                    else
                        summary.CommandsCreated++;
                }
            }

            if (bot.Status != BotStatus.Offline)
            {
                summary.Warnings.Add("The bot is running; restart it to apply the imported configuration.");
            }

            if (dryRun)
            {
                return summary;
            }

            // InMemory (unit tests) does not support transactions.
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(ct)
                : null;

            if (doc.Bot != null)
            {
                bot.Name = doc.Bot.Name!.Trim();
                bot.IsPublic = doc.Bot.IsPublic;
                bot.AutoStart = doc.Bot.AutoStart;
            }

            if (doc.Boards != null)
            {
                await ReplaceBoardsAsync(botId, doc.Boards, existingBoards, applyDiscordIds, ct);
            }

            if (doc.Commands != null)
            {
                var now = _utcNow();
                foreach (var command in doc.Commands)
                {
                    var name = command.CommandName!.Trim();
                    var subName = NormalizeNullable(command.SubCommandName);
                    if (!commandsByKey.TryGetValue(CommandKey(name, subName), out var entity))
                    {
                        entity = new BotCommand { BotId = botId, CommandName = name, SubCommandName = subName, CreatedAt = now };
                        _db.BotCommands.Add(entity);
                        commandsByKey[CommandKey(name, subName)] = entity;
                    }

                    entity.Description = command.Description;
                    entity.MinimumPermissionLevel = command.MinimumPermissionLevel;
                    entity.IsEnabled = command.IsEnabled;
                    entity.UserHint = command.UserHint;
                    entity.SuccessMessage = command.SuccessMessage;
                    entity.PermissionMessage = command.PermissionMessage;
                    entity.ErrorMessage = command.ErrorMessage;
                    entity.AdminOnlyMessage = command.AdminOnlyMessage;
                    entity.InvalidArgumentsMessage = command.InvalidArgumentsMessage;
                    entity.UpdatedAt = now;
                }
            }

            await _db.SaveChangesAsync(ct);
            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }

            return summary;
        }

        private async Task ReplaceBoardsAsync(
            int botId,
            List<ExportedBoard> boards,
            List<BoardConfiguration> existingBoards,
            bool applyDiscordIds,
            CancellationToken ct)
        {
            // The active-board FK is NO ACTION: release it before deleting the boards it may point to.
            var configuration = await _db.BotConfigurations.FirstOrDefaultAsync(c => c.BotId == botId, ct);
            if (configuration == null)
            {
                configuration = new BotConfiguration { BotId = botId };
                _db.BotConfigurations.Add(configuration);
            }
            configuration.ActiveBoardConfigurationId = null;
            await _db.SaveChangesAsync(ct);

            _db.Teams.RemoveRange(existingBoards.SelectMany(b => b.Teams));
            _db.BoardConfigurations.RemoveRange(existingBoards);

            var created = boards
                .Select(b => new BoardConfiguration
                {
                    BotId = botId,
                    BoardType = string.IsNullOrWhiteSpace(b.BoardType) ? "teams" : b.BoardType.Trim(),
                    GuildId = applyDiscordIds ? ParseNullableUlong(b.GuildId) : null,
                    BoardChannelId = applyDiscordIds ? ParseNullableUlong(b.BoardChannelId) : null,
                    BoardTitle = NormalizeNullable(b.BoardTitle),
                    BoardDescriptionTemplate = NormalizeNullable(b.BoardDescriptionTemplate),
                    SubtitleLabel = NormalizeNullable(b.SubtitleLabel),
                    ContactLabel = NormalizeNullable(b.ContactLabel),
                    Teams = (b.Teams ?? [])
                        .Select(t => new Team
                        {
                            Name = t.Name!.Trim(),
                            LeaderName = t.LeaderName?.Trim() ?? string.Empty,
                            CommanderContact = t.Contact?.Trim() ?? string.Empty,
                            Emoji = NormalizeEmoji(t.Emoji)
                        })
                        .ToList()
                })
                .ToList();

            _db.BoardConfigurations.AddRange(created);
            await _db.SaveChangesAsync(ct);

            if (created.Count > 0)
            {
                var activeIndex = boards.FindIndex(b => b.IsActive);
                configuration.ActiveBoardConfigurationId = created[activeIndex >= 0 ? activeIndex : 0].BoardConfigurationId;
            }
        }

        internal static string NormalizeEmoji(string? emoji)
            => string.IsNullOrWhiteSpace(emoji) ? DefaultTeamEmoji : emoji.Trim();

        private static string CommandKey(string commandName, string? subCommandName)
            => $"{commandName}\u001f{subCommandName}";

        private static string? NormalizeNullable(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static ulong? ParseNullableUlong(string? value)
            => ulong.TryParse(value, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Thrown when an import document fails validation.
    /// </summary>
    public sealed class BotConfigImportValidationException : Exception
    {
        public BotConfigImportValidationException(IReadOnlyList<string> errors)
            : base("Import document is invalid: " + string.Join("; ", errors))
        {
            Errors = errors;
        }

        public IReadOnlyList<string> Errors { get; }
    }

    /// <summary>
    /// Validates import documents against the entity limits (MaxLength etc.).
    /// </summary>
    public static class BotConfigImportValidator
    {
        public const int MaxBoards = 50;
        public const int MaxTeamsPerBoard = 100;
        public const int MaxCommands = 500;
        public const int MaxErrors = 50;

        /// <summary>
        /// Returns human-readable validation errors (empty when the document is valid).
        /// </summary>
        public static List<string> Validate(BotConfigExportDocument? document)
        {
            var errors = new List<string>();
            if (document == null)
            {
                errors.Add("Document is empty.");
                return errors;
            }

            if (document.SchemaVersion != BotConfigExportDocument.CurrentSchemaVersion)
            {
                errors.Add($"schemaVersion: unsupported value {document.SchemaVersion} (expected {BotConfigExportDocument.CurrentSchemaVersion}).");
                return errors;
            }

            if (document.Bot != null)
            {
                Required(errors, "bot.name", document.Bot.Name, 200);
            }

            if (document.Boards != null)
            {
                if (document.Boards.Count > MaxBoards)
                {
                    errors.Add($"boards: at most {MaxBoards} boards are allowed.");
                }

                if (document.Boards.Count(b => b.IsActive) > 1)
                {
                    errors.Add("boards: only one board can be active.");
                }

                for (var i = 0; i < document.Boards.Count && errors.Count < MaxErrors; i++)
                {
                    ValidateBoard(errors, $"boards[{i}]", document.Boards[i]);
                }
            }

            if (document.Commands != null)
            {
                if (document.Commands.Count > MaxCommands)
                {
                    errors.Add($"commands: at most {MaxCommands} commands are allowed.");
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < document.Commands.Count && errors.Count < MaxErrors; i++)
                {
                    var command = document.Commands[i];
                    var path = $"commands[{i}]";
                    if (command == null)
                    {
                        errors.Add($"{path}: must not be null.");
                        continue;
                    }

                    Required(errors, $"{path}.commandName", command.CommandName, 100);
                    Optional(errors, $"{path}.subCommandName", command.SubCommandName, 100);
                    Optional(errors, $"{path}.description", command.Description, 500);
                    Optional(errors, $"{path}.userHint", command.UserHint, 500);
                    Optional(errors, $"{path}.successMessage", command.SuccessMessage, 1000);
                    Optional(errors, $"{path}.permissionMessage", command.PermissionMessage, 500);
                    Optional(errors, $"{path}.errorMessage", command.ErrorMessage, 500);
                    Optional(errors, $"{path}.adminOnlyMessage", command.AdminOnlyMessage, 500);
                    Optional(errors, $"{path}.invalidArgumentsMessage", command.InvalidArgumentsMessage, 500);
                    if (command.MinimumPermissionLevel is < 0 or > 2)
                    {
                        errors.Add($"{path}.minimumPermissionLevel: must be 0, 1 or 2.");
                    }

                    if (!string.IsNullOrWhiteSpace(command.CommandName))
                    {
                        var name = command.CommandName.Trim();
                        var subName = string.IsNullOrWhiteSpace(command.SubCommandName) ? null : command.SubCommandName.Trim();
                        if (!seen.Add($"{name}\u001f{subName}"))
                        {
                            errors.Add($"{path}: duplicate command '{(subName == null ? name : $"{name} {subName}")}'.");
                        }
                    }
                }
            }

            if (errors.Count > MaxErrors)
            {
                errors.RemoveRange(MaxErrors, errors.Count - MaxErrors);
            }

            return errors;
        }

        private static void ValidateBoard(List<string> errors, string path, ExportedBoard? board)
        {
            if (board == null)
            {
                errors.Add($"{path}: must not be null.");
                return;
            }

            Optional(errors, $"{path}.boardType", board.BoardType, 50);
            Optional(errors, $"{path}.boardTitle", board.BoardTitle, 250);
            Optional(errors, $"{path}.boardDescriptionTemplate", board.BoardDescriptionTemplate, 500);
            Optional(errors, $"{path}.subtitleLabel", board.SubtitleLabel, 100);
            Optional(errors, $"{path}.contactLabel", board.ContactLabel, 100);
            DiscordId(errors, $"{path}.guildId", board.GuildId);
            DiscordId(errors, $"{path}.boardChannelId", board.BoardChannelId);

            var teams = board.Teams ?? [];
            if (teams.Count > BotConfigImportValidator.MaxTeamsPerBoard)
            {
                errors.Add($"{path}.teams: at most {MaxTeamsPerBoard} teams are allowed per board.");
                return;
            }

            var emojis = new HashSet<string>(StringComparer.Ordinal);
            for (var j = 0; j < teams.Count; j++)
            {
                var team = teams[j];
                var teamPath = $"{path}.teams[{j}]";
                if (team == null)
                {
                    errors.Add($"{teamPath}: must not be null.");
                    continue;
                }

                Required(errors, $"{teamPath}.name", team.Name, 200);
                Optional(errors, $"{teamPath}.leaderName", team.LeaderName, 200);
                Optional(errors, $"{teamPath}.contact", team.Contact, 200);
                Optional(errors, $"{teamPath}.emoji", team.Emoji, 50);

                var emoji = BotConfigTransferService.NormalizeEmoji(team.Emoji);
                if (!emojis.Add(emoji))
                {
                    errors.Add($"{teamPath}.emoji: duplicate emoji '{emoji}' on this board.");
                }
            }
        }

        private static void Required(List<string> errors, string path, string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{path}: is required.");
                return;
            }

            Optional(errors, path, value, maxLength);
        }

        private static void Optional(List<string> errors, string path, string? value, int maxLength)
        {
            if (value != null && value.Trim().Length > maxLength)
            {
                errors.Add($"{path}: must be at most {maxLength} characters.");
            }
        }

        private static void DiscordId(List<string> errors, string path, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !ulong.TryParse(value, out _))
            {
                errors.Add($"{path}: must be a Discord snowflake id.");
            }
        }
    }
}
