using BotManager.Backend.API.Models;
using BotManager.Backend.API.Services;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BotManager.Backend.API.Tests;

public class BotConfigTransferServiceTests
{
    [Fact]
    public void Validate_ValidDocument_HasNoErrors()
    {
        Assert.Empty(BotConfigImportValidator.Validate(ValidDocument()));
    }

    [Fact]
    public void Validate_NullDocument_IsRejected()
    {
        Assert.NotEmpty(BotConfigImportValidator.Validate(null));
    }

    [Fact]
    public void Validate_UnsupportedSchemaVersion_IsRejected()
    {
        var doc = ValidDocument();
        doc.SchemaVersion = 2;

        var error = Assert.Single(BotConfigImportValidator.Validate(doc));
        Assert.StartsWith("schemaVersion", error);
    }

    [Fact]
    public void Validate_DuplicateEmojisOnOneBoard_AreRejected_IncludingDefaultEmoji()
    {
        var doc = ValidDocument();
        doc.Boards![0].Teams =
        [
            new ExportedTeam { Name = "A", Emoji = "🎯" },
            new ExportedTeam { Name = "B", Emoji = null }, // defaults to 🎯
            new ExportedTeam { Name = "C", Emoji = "✅" }
        ];

        var errors = BotConfigImportValidator.Validate(doc);

        Assert.Contains(errors, e => e.StartsWith("boards[0].teams[1].emoji") && e.Contains("duplicate"));
    }

    [Fact]
    public void Validate_SameEmojiOnDifferentBoards_IsAllowed()
    {
        var doc = ValidDocument();
        doc.Boards!.Add(new ExportedBoard { Teams = [new ExportedTeam { Name = "Z", Emoji = "✅" }] });

        Assert.Empty(BotConfigImportValidator.Validate(doc));
    }

    [Fact]
    public void Validate_TooManyTeams_IsRejected()
    {
        var doc = ValidDocument();
        doc.Boards![0].Teams = Enumerable.Range(0, 101)
            .Select(i => new ExportedTeam { Name = $"T{i}", Emoji = $"e{i}" })
            .ToList();

        Assert.Contains(BotConfigImportValidator.Validate(doc), e => e.Contains("at most 100 teams"));
    }

    [Fact]
    public void Validate_LengthsAndRequiredFields_FollowEntityLimits()
    {
        var doc = ValidDocument();
        doc.Bot!.Name = new string('n', 201);
        doc.Boards![0].BoardTitle = new string('t', 251);
        doc.Boards[0].Teams![0].Name = " ";
        doc.Boards[0].Teams![1].Emoji = new string('e', 51);
        doc.Commands![0].SuccessMessage = new string('s', 1001);
        doc.Commands[0].MinimumPermissionLevel = 5;

        var errors = BotConfigImportValidator.Validate(doc);

        Assert.Contains(errors, e => e.StartsWith("bot.name"));
        Assert.Contains(errors, e => e.StartsWith("boards[0].boardTitle"));
        Assert.Contains(errors, e => e.StartsWith("boards[0].teams[0].name") && e.Contains("required"));
        Assert.Contains(errors, e => e.StartsWith("boards[0].teams[1].emoji"));
        Assert.Contains(errors, e => e.StartsWith("commands[0].successMessage"));
        Assert.Contains(errors, e => e.StartsWith("commands[0].minimumPermissionLevel"));
    }

    [Fact]
    public void Validate_InvalidDiscordIds_MultipleActiveBoards_AndDuplicateCommands_AreRejected()
    {
        var doc = ValidDocument();
        doc.Boards![0].GuildId = "not-a-number";
        doc.Boards.Add(new ExportedBoard { IsActive = true });
        doc.Commands!.Add(new ExportedCommand { CommandName = "board", SubCommandName = "add-team" });

        var errors = BotConfigImportValidator.Validate(doc);

        Assert.Contains(errors, e => e.StartsWith("boards[0].guildId"));
        Assert.Contains(errors, e => e.Contains("only one board can be active"));
        Assert.Contains(errors, e => e.Contains("duplicate command 'board add-team'"));
    }

    [Fact]
    public async Task ImportAsync_InvalidDocument_ThrowsAndWritesNothing()
    {
        await using var db = CreateContext();
        await SeedBotAsync(db, 1);
        var doc = ValidDocument();
        doc.SchemaVersion = 99;

        await Assert.ThrowsAsync<BotConfigImportValidationException>(() => new BotConfigTransferService(db).ImportAsync(1, doc));
        Assert.Equal("original", (await db.Bots.AsNoTracking().SingleAsync()).Name);
    }

    [Fact]
    public async Task ExportThenImport_RoundTripsIntoAnotherBot()
    {
        await using var db = CreateContext();
        await SeedBotAsync(db, 1);
        db.Bots.Add(new Bot { BotId = 2, Name = "target", BotToken = "secret-2", OwnerUserId = "owner" });
        db.BoardConfigurations.Add(new BoardConfiguration { BoardConfigurationId = 50, BotId = 2, BoardMessageId = 9 });
        db.Teams.Add(new Team { BoardConfigurationId = 50, Name = "Old", Emoji = "🐢", RoleId = 1234 });
        db.BotCommands.Add(new BotCommand { BotId = 2, CommandName = "board", SubCommandName = "add-team", Description = "old" });
        await db.SaveChangesAsync();

        var sut = new BotConfigTransferService(db);
        var exported = await sut.ExportAsync(1);
        Assert.NotNull(exported);

        var dry = await sut.ImportAsync(2, exported, dryRun: true);
        Assert.True(dry.DryRun);
        Assert.Equal(1, dry.BoardsRemoved);
        Assert.Equal("target", (await db.Bots.AsNoTracking().SingleAsync(b => b.BotId == 2)).Name);

        var summary = await sut.ImportAsync(2, exported);

        Assert.Equal(2, summary.BoardsImported);
        Assert.Equal(3, summary.TeamsImported);
        Assert.Equal(1, summary.CommandsUpdated);
        Assert.Equal(0, summary.CommandsCreated);
        Assert.Contains(summary.Warnings, w => w.Contains("role bindings"));

        var reExported = await sut.ExportAsync(2);
        Assert.Equal("original", reExported!.Bot!.Name);
        Assert.True(reExported.Bot.IsPublic);
        Assert.Equal(exported!.Boards!.Select(b => (b.BoardTitle, b.IsActive, b.GuildId, b.BoardChannelId)),
            reExported.Boards!.Select(b => (b.BoardTitle, b.IsActive, b.GuildId, b.BoardChannelId)));
        Assert.Equal(exported.Boards!.SelectMany(b => b.Teams!).Select(t => (t.Name, t.Emoji, t.LeaderName, t.Contact)),
            reExported.Boards!.SelectMany(b => b.Teams!).Select(t => (t.Name, t.Emoji, t.LeaderName, t.Contact)));
        var command = Assert.Single(reExported.Commands!);
        Assert.Equal("Custom description", command.Description);
        Assert.False(command.IsEnabled);

        // Old board and team are gone; the token is untouched; no role ids leaked into new teams.
        Assert.False(await db.BoardConfigurations.AnyAsync(b => b.BoardConfigurationId == 50));
        Assert.False(await db.Teams.AnyAsync(t => t.Name == "Old"));
        Assert.All(await db.Teams.Where(t => t.BoardConfiguration.BotId == 2).ToListAsync(), t => Assert.Null(t.RoleId));
        Assert.Equal("secret-2", (await db.Bots.AsNoTracking().SingleAsync(b => b.BotId == 2)).BotToken);
    }

    [Fact]
    public async Task ImportAsync_WithoutDiscordIds_LeavesIdsEmpty_AndActivatesFirstBoardByDefault()
    {
        await using var db = CreateContext();
        await SeedBotAsync(db, 1);
        var doc = ValidDocument();
        doc.Boards![0].IsActive = false;

        await new BotConfigTransferService(db).ImportAsync(1, doc, applyDiscordIds: false);

        var board = await db.BoardConfigurations.AsNoTracking().SingleAsync(b => b.BotId == 1);
        Assert.Null(board.GuildId);
        Assert.Null(board.BoardChannelId);
        var config = await db.BotConfigurations.AsNoTracking().SingleAsync(c => c.BotId == 1);
        Assert.Equal(board.BoardConfigurationId, config.ActiveBoardConfigurationId);
    }

    [Fact]
    public async Task ImportAsync_NullSections_LeaveThatPartUntouched()
    {
        await using var db = CreateContext();
        await SeedBotAsync(db, 1);

        var summary = await new BotConfigTransferService(db).ImportAsync(1,
            new BotConfigExportDocument { SchemaVersion = 1, Commands = [new ExportedCommand { CommandName = "new-cmd" }] });

        Assert.Equal(1, summary.CommandsCreated);
        Assert.Equal(2, await db.BoardConfigurations.CountAsync(b => b.BotId == 1));
        Assert.Equal("original", (await db.Bots.AsNoTracking().SingleAsync()).Name);
    }

    [Fact]
    public async Task ExportAsync_UnknownBot_ReturnsNull()
    {
        await using var db = CreateContext();
        Assert.Null(await new BotConfigTransferService(db).ExportAsync(404));
    }

    private static async Task SeedBotAsync(BotManagerDbContext db, int botId)
    {
        db.Bots.Add(new Bot { BotId = botId, Name = "original", BotToken = "secret", OwnerUserId = "owner", IsPublic = true });
        db.BoardConfigurations.AddRange(
            new BoardConfiguration { BoardConfigurationId = 10, BotId = botId, BoardTitle = "First", GuildId = 111, BoardChannelId = 222, BoardMessageId = 333 },
            new BoardConfiguration { BoardConfigurationId = 11, BotId = botId, BoardTitle = "Second" });
        db.BotConfigurations.Add(new BotConfiguration { BotId = botId, ActiveBoardConfigurationId = 11 });
        db.Teams.AddRange(
            new Team { BoardConfigurationId = 10, Name = "Alpha", LeaderName = "Al", CommanderContact = "@al", Emoji = "✅", RoleId = 999 },
            new Team { BoardConfigurationId = 10, Name = "Bravo", Emoji = "🎯" },
            new Team { BoardConfigurationId = 11, Name = "Charlie", Emoji = "✅" });
        db.BotCommands.Add(new BotCommand
        {
            BotId = botId,
            CommandName = "board",
            SubCommandName = "add-team",
            Description = "Custom description",
            MinimumPermissionLevel = 1,
            IsEnabled = false
        });
        await db.SaveChangesAsync();
    }

    private static BotConfigExportDocument ValidDocument() => new()
    {
        SchemaVersion = 1,
        Bot = new ExportedBotSettings { Name = "Imported", IsPublic = false, AutoStart = true },
        Boards =
        [
            new ExportedBoard
            {
                BoardType = "teams",
                GuildId = "123456789012345678",
                BoardChannelId = "223456789012345678",
                BoardTitle = "Title",
                IsActive = true,
                Teams =
                [
                    new ExportedTeam { Name = "A", LeaderName = "L", Contact = "C", Emoji = "🎯" },
                    new ExportedTeam { Name = "B", Emoji = "✅" }
                ]
            }
        ],
        Commands = [new ExportedCommand { CommandName = "board", SubCommandName = "add-team", MinimumPermissionLevel = 1 }]
    };

    private static BotManagerDbContext CreateContext()
        => new(new DbContextOptionsBuilder<BotManagerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
