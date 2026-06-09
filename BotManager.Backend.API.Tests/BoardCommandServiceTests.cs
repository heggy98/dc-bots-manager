using BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Handlers;
using BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Services;
using BotManager.Backend.API.Services;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Models;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Shared.Models;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BotManager.Backend.API.Tests;

public class BoardCommandServiceTests
{
    [Fact]
    public void BuildCommands_ReturnsSingleBoardParentCommand()
    {
        var sut = CreateSut();

        var commands = sut.BuildCommands();

        var command = Assert.Single(commands);
        Assert.IsType<SlashCommandProperties>(command);
    }

    [Fact]
    public void BuildCommands_ParentCommand_HasExpectedNameDescriptionAndSubcommandCount()
    {
        var sut = CreateSut();

        var slash = Assert.IsType<SlashCommandProperties>(Assert.Single(sut.BuildCommands()));

        Assert.Equal("board", slash.Name.ToString());
        Assert.Equal("Board management commands", slash.Description.ToString());
        Assert.True(slash.Options.IsSpecified);
        Assert.Equal(6, slash.Options.Value.Count);
    }

    [Fact]
    public void BuildCommands_ParentCommand_ContainsExpectedSubcommandNames()
    {
        var sut = CreateSut();

        var slash = Assert.IsType<SlashCommandProperties>(Assert.Single(sut.BuildCommands()));
        var names = slash.Options.Value
            .Select(option => option.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            ["add-team", "edit-team", "insert", "refresh", "remove-team", "sync-reactions"],
            names);
    }

    [Fact]
    public void BuildCommands_AddTeamSubcommand_HasThreeRequiredOptions()
    {
        var sut = CreateSut();

        var slash = Assert.IsType<SlashCommandProperties>(Assert.Single(sut.BuildCommands()));
        var addTeam = Assert.Single(slash.Options.Value, option => option.Name == "add-team");

        Assert.Equal(3, addTeam.Options.Count);
        Assert.All(addTeam.Options, option => Assert.True(option.IsRequired));
    }

    [Fact]
    public void BuildCommands_AllTopLevelOptions_AreSubcommands()
    {
        var sut = CreateSut();

        var slash = Assert.IsType<SlashCommandProperties>(Assert.Single(sut.BuildCommands()));

        Assert.All(slash.Options.Value, option => Assert.Equal(ApplicationCommandOptionType.SubCommand, option.Type));
    }

    [Fact]
    public void BuildCommands_RemoveTeamSubcommand_HasRequiredNameOption()
    {
        var sut = CreateSut();

        var slash = Assert.IsType<SlashCommandProperties>(Assert.Single(sut.BuildCommands()));
        var removeTeam = Assert.Single(slash.Options.Value, option => option.Name == "remove-team");

        var nameOption = Assert.Single(removeTeam.Options, option => option.Name == "name");
        Assert.Equal(ApplicationCommandOptionType.String, nameOption.Type);
        Assert.True(nameOption.IsRequired);
    }

    [Fact]
    public void BuildCommands_EditTeamSubcommand_EnforcesRequiredAndOptionalArguments()
    {
        var sut = CreateSut();

        var slash = Assert.IsType<SlashCommandProperties>(Assert.Single(sut.BuildCommands()));
        var editTeam = Assert.Single(slash.Options.Value, option => option.Name == "edit-team");

        Assert.Equal(4, editTeam.Options.Count);

        var name = Assert.Single(editTeam.Options, option => option.Name == "name");
        Assert.True(name.IsRequired);

        var newName = Assert.Single(editTeam.Options, option => option.Name == "new-name");
        var newLeader = Assert.Single(editTeam.Options, option => option.Name == "new-leader");
        var newContact = Assert.Single(editTeam.Options, option => option.Name == "new-contact");

        Assert.False(newName.IsRequired);
        Assert.False(newLeader.IsRequired);
        Assert.False(newContact.IsRequired);
    }

    [Fact]
    public void BuildCommands_AdminSubcommands_HaveNoArguments()
    {
        var sut = CreateSut();

        var slash = Assert.IsType<SlashCommandProperties>(Assert.Single(sut.BuildCommands()));

        var refresh = Assert.Single(slash.Options.Value, option => option.Name == "refresh");
        var insert = Assert.Single(slash.Options.Value, option => option.Name == "insert");
        var syncReactions = Assert.Single(slash.Options.Value, option => option.Name == "sync-reactions");

        Assert.Empty(refresh.Options);
        Assert.Empty(insert.Options);
        Assert.Empty(syncReactions.Options);
    }

    [Fact]
    public void GetCommandRegistrations_ReturnsExpectedSubcommandsWithoutParent()
    {
        var sut = CreateSut();

        var registrations = sut.GetCommandRegistrations();

        Assert.Equal(6, registrations.Count);
        Assert.All(registrations, r => Assert.Equal("board", r.Name));
        Assert.DoesNotContain(registrations, r => r.SubCommandName is null);

        var subcommands = registrations
            .Select(r => r.SubCommandName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Cast<string>()
            .OrderBy(s => s)
            .ToArray();

        Assert.Equal(
            ["add-team", "edit-team", "insert", "refresh", "remove-team", "sync-reactions"],
            subcommands);
    }

    [Fact]
    public void GetCommandRegistrations_AdminSubcommands_HaveMinPermissionLevelOne()
    {
        var sut = CreateSut();

        var registrations = sut.GetCommandRegistrations();
        var adminCommands = registrations
            .Where(r => r.SubCommandName is "refresh" or "insert" or "sync-reactions")
            .ToList();

        Assert.Equal(3, adminCommands.Count);
        Assert.All(adminCommands, cmd => Assert.Equal(1, cmd.MinPermissionLevel));
    }

    [Fact]
    public void GetCommandRegistrations_InsertSubcommand_HasExpectedMetadata()
    {
        var sut = CreateSut();

        var insert = Assert.Single(sut.GetCommandRegistrations(), r => r.SubCommandName == "insert");

        Assert.Equal("Inserts the board into the configured channel.", insert.Description);
        Assert.Equal("Use /board insert.", insert.UserHint);
        Assert.Equal("Board was inserted.", insert.SuccessMessage);
        Assert.Equal("This command is admin-only.", insert.PermissionMessage);
        Assert.Equal("Error: {0}", insert.ErrorMessage);
    }

    private static BoardCommandService CreateSut()
    {
        var audit = new FakeBoardCommandAuditService();
        var emojiCatalog = new FakeEmojiCatalogService();
        var boardLocator = new FakeBoardMessageLocator();

        var teams = new TeamsCommandHandler(audit, emojiCatalog);
        var reactions = new ReactionsCommandHandler(audit, boardLocator);

        return new BoardCommandService(teams, reactions, NullLogger<BoardCommandService>.Instance);
    }

    private sealed class FakeBoardCommandAuditService : IBoardCommandAuditService
    {
        public Task<BotCommand?> GetCommandAsync(string commandName, IPluginContext context)
        {
            return Task.FromResult<BotCommand?>(null);
        }

        public Task LogCommandUsageAsync(BotCommand? command, IUser user, bool isSuccess, string? errorMessage, IPluginContext context)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmojiCatalogService : IEmojiCatalogService
    {
        public Task<IReadOnlyList<string>> GetEmojiCatalogAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<string> values = ["🎯", "✅", "🛡️"];
            return Task.FromResult(values);
        }
    }

    private sealed class FakeBoardMessageLocator : IBoardMessageLocator
    {
        public Task<IUserMessage?> FindBoardMessageAsync(SocketTextChannel channel, string marker, int historyLimit = 100)
        {
            return Task.FromResult<IUserMessage?>(null);
        }
    }
}
