using BotManager.Backend.API.Services;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BotManager.Backend.API.Tests;

public class DbBotDataServiceTests
{
    [Fact]
    public async Task GetAsync_NoBoardConfiguration_ReturnsGlobalDefaults()
    {
        await using var db = CreateContext();
        db.Bots.Add(CreateBot(101));
        db.BoardGlobalConfigs.Add(new BoardGlobalConfig
        {
            Id = 1,
            DefaultBoardTitle = "Default title",
            DefaultBoardDescription = "Default description",
            DefaultBoardDetailSubtitleLabel = "Default subtitle",
            DefaultBoardDetailContactLabel = "Default contact"
        });
        await db.SaveChangesAsync();

        var sut = new DbBotDataService(db);

        var result = await sut.GetAsync(101);

        Assert.Null(result.ActiveBoardConfigurationId);
        Assert.Equal("Default title", result.BoardTitle);
        Assert.Equal("Default description", result.BoardDescriptionTemplate);
        Assert.Equal("Default subtitle", result.SubtitleLabel);
        Assert.Equal("Default contact", result.ContactLabel);
    }

    [Fact]
    public async Task GetAsync_ActiveBoardMissing_FallsBackToFirstBoardConfiguration()
    {
        await using var db = CreateContext();
        db.Bots.Add(CreateBot(102));
        db.BotConfigurations.Add(new BotConfiguration
        {
            BotId = 102,
            ActiveBoardConfigurationId = 9999
        });
        db.BoardConfigurations.AddRange(
            new BoardConfiguration
            {
                BoardConfigurationId = 2,
                BotId = 102,
                BoardType = "teams",
                BoardTitle = "Second"
            },
            new BoardConfiguration
            {
                BoardConfigurationId = 1,
                BotId = 102,
                BoardType = "teams",
                BoardTitle = "First"
            });
        await db.SaveChangesAsync();

        var sut = new DbBotDataService(db);

        var result = await sut.GetAsync(102);

        Assert.Equal("First", result.BoardTitle);
        Assert.Equal("teams", result.BoardType);
    }

    [Fact]
    public async Task SaveAsync_NoBoardConfiguration_CreatesBoardAndSetsBotActiveBoard()
    {
        await using var db = CreateContext();
        db.Bots.Add(CreateBot(103));
        await db.SaveChangesAsync();

        var sut = new DbBotDataService(db);

        await sut.SaveAsync(103, new BotConfigurationDto
        {
            BoardType = null,
            BoardChannelId = "123",
            BoardMessageId = "bad-number",
            BoardTitle = "  Title  ",
            BoardDescriptionTemplate = "  Desc  ",
            SubtitleLabel = "  Subtitle  ",
            ContactLabel = "  Contact  "
        });

        var board = Assert.Single(db.BoardConfigurations.Where(c => c.BotId == 103));
        var botConfig = Assert.Single(db.BotConfigurations.Where(c => c.BotId == 103));

        Assert.Equal("teams", board.BoardType);
        Assert.Equal(123UL, board.BoardChannelId);
        Assert.Null(board.BoardMessageId);
        Assert.Equal("Title", board.BoardTitle);
        Assert.Equal("Desc", board.BoardDescriptionTemplate);
        Assert.Equal("Subtitle", board.SubtitleLabel);
        Assert.Equal("Contact", board.ContactLabel);
        Assert.Equal(board.BoardConfigurationId, botConfig.ActiveBoardConfigurationId);
    }

    [Fact]
    public async Task SaveAsync_RequestedActiveBoardExists_UsesRequestedBoardAsActive()
    {
        await using var db = CreateContext();
        db.Bots.Add(CreateBot(104));
        db.BoardConfigurations.AddRange(
            new BoardConfiguration
            {
                BoardConfigurationId = 10,
                BotId = 104,
                BoardType = "teams",
                BoardTitle = "A"
            },
            new BoardConfiguration
            {
                BoardConfigurationId = 20,
                BotId = 104,
                BoardType = "teams",
                BoardTitle = "B"
            });
        db.BotConfigurations.Add(new BotConfiguration
        {
            BotId = 104,
            ActiveBoardConfigurationId = 10
        });
        await db.SaveChangesAsync();

        var sut = new DbBotDataService(db);

        await sut.SaveAsync(104, new BotConfigurationDto
        {
            ActiveBoardConfigurationId = 20,
            BoardType = "projects"
        });

        var botConfig = Assert.Single(db.BotConfigurations.Where(c => c.BotId == 104));
        Assert.Equal(20, botConfig.ActiveBoardConfigurationId);
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
