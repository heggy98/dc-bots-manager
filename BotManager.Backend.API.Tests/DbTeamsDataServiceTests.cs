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

    [Fact]
    public async Task SaveAsync_PreservesRoleBinding_WhenClientDoesNotSendRoleId()
    {
        await using var db = CreateContext();
        db.Bots.Add(CreateBot(203));
        db.BoardConfigurations.Add(new BoardConfiguration
        {
            BoardConfigurationId = 1,
            BotId = 203,
            BoardType = "teams"
        });
        db.Teams.Add(new Team
        {
            TeamId = 10,
            BoardConfigurationId = 1,
            Name = "Alpha",
            Emoji = "✅",
            RoleId = 123456789012345678UL
        });
        await db.SaveChangesAsync();

        var sut = new DbTeamsDataService(db);

        // Simulates the admin UI: RoleId is never part of the client payload.
        await sut.SaveAsync(203, new BotTeamsDto
        {
            Teams =
            [
                new TeamDto { TeamId = 10, Name = "Alpha renamed", Emoji = "✅" },
                new TeamDto { Name = "Beta", Emoji = "🎯" }
            ]
        });

        var teams = (await sut.GetAsync(203)).Teams;
        Assert.Equal(123456789012345678UL, teams.Single(t => t.Name == "Alpha renamed").RoleId);
        Assert.Null(teams.Single(t => t.Name == "Beta").RoleId);
    }

    [Fact]
    public async Task SaveAsync_KeepsTeamIdsStableAcrossSaves()
    {
        await using var db = CreateContext();
        db.Bots.Add(CreateBot(205));
        db.BoardConfigurations.Add(new BoardConfiguration { BoardConfigurationId = 1, BotId = 205, BoardType = "teams" });
        await db.SaveChangesAsync();

        var sut = new DbTeamsDataService(db);

        await sut.SaveAsync(205, new BotTeamsDto
        {
            Teams =
            [
                new TeamDto { Name = "Alpha", Emoji = "✅" },
                new TeamDto { Name = "Beta", Emoji = "🎯" }
            ]
        });

        var firstLoad = (await sut.GetAsync(205)).Teams;
        var alphaId = firstLoad.Single(t => t.Name == "Alpha").TeamId;
        var betaId = firstLoad.Single(t => t.Name == "Beta").TeamId;
        Assert.NotNull(alphaId);
        Assert.NotNull(betaId);

        // Round-trip what the client loaded, with edits (rename, emoji swap) and one new team.
        firstLoad.Single(t => t.TeamId == alphaId).Name = "Alpha renamed";
        firstLoad.Single(t => t.TeamId == alphaId).Emoji = "🎯";
        firstLoad.Single(t => t.TeamId == betaId).Emoji = "✅";
        firstLoad.Add(new TeamDto { Name = "Gamma", Emoji = "🔥" });
        await sut.SaveAsync(205, new BotTeamsDto { Teams = firstLoad });

        var secondLoad = (await sut.GetAsync(205)).Teams;
        Assert.Equal(3, secondLoad.Count);
        var alpha = secondLoad.Single(t => t.TeamId == alphaId);
        Assert.Equal("Alpha renamed", alpha.Name);
        Assert.Equal("🎯", alpha.Emoji);
        Assert.Equal("✅", secondLoad.Single(t => t.TeamId == betaId).Emoji);
        var gamma = secondLoad.Single(t => t.Name == "Gamma");
        Assert.NotNull(gamma.TeamId);
        Assert.DoesNotContain(gamma.TeamId, new[] { alphaId, betaId });
    }

    [Fact]
    public async Task SaveAsync_RemovedTeamIsDeleted_AndRemainingTeamKeepsRoleBinding()
    {
        await using var db = CreateContext();
        db.Bots.Add(CreateBot(206));
        db.BoardConfigurations.Add(new BoardConfiguration { BoardConfigurationId = 1, BotId = 206, BoardType = "teams" });
        db.Teams.AddRange(
            new Team { TeamId = 20, BoardConfigurationId = 1, Name = "Alpha", Emoji = "✅", RoleId = 111UL },
            new Team { TeamId = 21, BoardConfigurationId = 1, Name = "Beta", Emoji = "🎯", RoleId = 222UL });
        await db.SaveChangesAsync();

        var sut = new DbTeamsDataService(db);

        await sut.SaveAsync(206, new BotTeamsDto
        {
            Teams = [new TeamDto { TeamId = 21, Name = "Beta", Emoji = "🎯" }]
        });

        var teams = await db.Teams.AsNoTracking().Where(t => t.BoardConfigurationId == 1).ToListAsync();
        var remaining = Assert.Single(teams);
        Assert.Equal(21, remaining.TeamId);
        Assert.Equal(222UL, remaining.RoleId);
        Assert.False(await db.Teams.AnyAsync(t => t.TeamId == 20));
    }

    [Fact]
    public async Task SaveAsync_TeamIdOfAnotherBoard_IsTreatedAsNewTeam()
    {
        await using var db = CreateContext();
        db.Bots.Add(CreateBot(207));
        db.Bots.Add(CreateBot(208));
        db.BoardConfigurations.Add(new BoardConfiguration { BoardConfigurationId = 1, BotId = 207, BoardType = "teams" });
        db.BoardConfigurations.Add(new BoardConfiguration { BoardConfigurationId = 2, BotId = 208, BoardType = "teams" });
        db.Teams.Add(new Team { TeamId = 30, BoardConfigurationId = 2, Name = "Foreign", Emoji = "✅", RoleId = 333UL });
        await db.SaveChangesAsync();

        var sut = new DbTeamsDataService(db);

        // Bot 207 tries to save a team carrying the id of bot 208's team.
        await sut.SaveAsync(207, new BotTeamsDto
        {
            Teams = [new TeamDto { TeamId = 30, Name = "Hijacked", Emoji = "🎯" }]
        });

        var foreign = await db.Teams.AsNoTracking().SingleAsync(t => t.TeamId == 30);
        Assert.Equal(2, foreign.BoardConfigurationId);
        Assert.Equal("Foreign", foreign.Name);
        Assert.Equal("✅", foreign.Emoji);
        Assert.Equal(333UL, foreign.RoleId);

        var own = Assert.Single(await db.Teams.AsNoTracking().Where(t => t.BoardConfigurationId == 1).ToListAsync());
        Assert.NotEqual(30, own.TeamId);
        Assert.Equal("Hijacked", own.Name);
        Assert.Null(own.RoleId);
    }

    [Fact]
    public async Task SaveAsync_NewTeamWithoutId_InheritsRoleOfRemovedTeamWithSameName()
    {
        await using var db = CreateContext();
        db.Bots.Add(CreateBot(209));
        db.BoardConfigurations.Add(new BoardConfiguration { BoardConfigurationId = 1, BotId = 209, BoardType = "teams" });
        db.Teams.Add(new Team { TeamId = 40, BoardConfigurationId = 1, Name = "Alpha", Emoji = "✅", RoleId = 444UL });
        await db.SaveChangesAsync();

        var sut = new DbTeamsDataService(db);

        // A client that does not send ids at all.
        await sut.SaveAsync(209, new BotTeamsDto
        {
            Teams = [new TeamDto { Name = "Alpha", Emoji = "✅" }]
        });

        var team = Assert.Single(await db.Teams.AsNoTracking().Where(t => t.BoardConfigurationId == 1).ToListAsync());
        Assert.Equal(444UL, team.RoleId);
    }

    [Fact]
    public async Task SaveAsync_ExplicitRoleIdOverridesStoredBinding()
    {
        await using var db = CreateContext();
        db.Bots.Add(CreateBot(210));
        db.BoardConfigurations.Add(new BoardConfiguration { BoardConfigurationId = 1, BotId = 210, BoardType = "teams" });
        db.Teams.Add(new Team { TeamId = 50, BoardConfigurationId = 1, Name = "Alpha", Emoji = "✅", RoleId = 555UL });
        await db.SaveChangesAsync();

        var sut = new DbTeamsDataService(db);

        await sut.SaveAsync(210, new BotTeamsDto
        {
            Teams = [new TeamDto { TeamId = 50, Name = "Alpha", Emoji = "✅", RoleId = 666UL }]
        });

        var team = await db.Teams.AsNoTracking().SingleAsync(t => t.TeamId == 50);
        Assert.Equal(666UL, team.RoleId);
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
