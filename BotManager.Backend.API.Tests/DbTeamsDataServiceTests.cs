using BotManager.Backend.API.Services;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BotManager.Backend.API.Tests;

public class DbTeamsDataServiceTests
{
    [Fact]
    public async Task SaveAsync_DuplicateEmojis_ThrowsInvalidOperationException()
    {
        await using var db = CreateContext();
        db.Bots.Add(CreateBot(201));
        db.BoardConfigurations.Add(new BoardConfiguration
        {
            BoardConfigurationId = 1,
            BotId = 201,
            BoardType = "teams"
        });
        await db.SaveChangesAsync();

        var sut = new DbTeamsDataService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.SaveAsync(201, new BotTeamsDto
            {
                Teams =
                [
                    new TeamDto { Name = "A", Emoji = "🎯" },
                    new TeamDto { Name = "B", Emoji = "🎯" }
                ]
            }));
    }

    [Fact]
    public async Task SaveAsync_ExistingTeamsAreReplaced_AndMissingEmojiGetsDefault()
    {
        await using var db = CreateContext();
        db.Bots.Add(CreateBot(202));
        db.BoardConfigurations.Add(new BoardConfiguration
        {
            BoardConfigurationId = 1,
            BotId = 202,
            BoardType = "teams"
        });
        db.Teams.Add(new Team
        {
            BoardConfigurationId = 1,
            Name = "Old Team",
            LeaderName = "Old Leader",
            CommanderContact = "Old Contact",
            Emoji = "✅"
        });
        await db.SaveChangesAsync();

        var sut = new DbTeamsDataService(db);

        await sut.SaveAsync(202, new BotTeamsDto
        {
            Teams =
            [
                new TeamDto { Name = "New Team", LeaderName = "L", Contact = "C", Emoji = "   " }
            ]
        });

        var teams = await db.Teams.Where(t => t.BoardConfigurationId == 1).ToListAsync();
        var team = Assert.Single(teams);

        Assert.Equal("New Team", team.Name);
        Assert.Equal("🎯", team.Emoji);
    }

    [Fact]
    public async Task GetAsync_NoBoardConfiguration_CreatesDefaultBoardAndReturnsNoTeams()
    {
        await using var db = CreateContext();
        db.Bots.Add(CreateBot(203));
        await db.SaveChangesAsync();

        var sut = new DbTeamsDataService(db);

        var result = await sut.GetAsync(203);

        Assert.Empty(result.Teams);

        var board = Assert.Single(db.BoardConfigurations.Where(c => c.BotId == 203));
        Assert.Equal("teams", board.BoardType);

        var botConfig = Assert.Single(db.BotConfigurations.Where(c => c.BotId == 203));
        Assert.Equal(board.BoardConfigurationId, botConfig.ActiveBoardConfigurationId);
    }

    [Fact]
    public async Task SaveAsync_ExplicitBoardIdMissing_ThrowsKeyNotFoundException()
    {
        await using var db = CreateContext();
        db.Bots.Add(CreateBot(204));
        db.BoardConfigurations.Add(new BoardConfiguration
        {
            BoardConfigurationId = 10,
            BotId = 204,
            BoardType = "teams"
        });
        await db.SaveChangesAsync();

        var sut = new DbTeamsDataService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            sut.SaveAsync(204, new BotTeamsDto { Teams = [] }, boardConfigurationId: 999));
    }

    private static BotManagerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BotManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new BotManagerDbContext(options);
    }

    private static Bot CreateBot(int botId)
    {
        return new Bot
        {
            BotId = botId,
            Name = $"bot-{botId}",
            BotToken = "token",
            OwnerUserId = "owner"
        };
    }
}
