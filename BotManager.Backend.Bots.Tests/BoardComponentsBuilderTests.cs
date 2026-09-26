using BotManager.Backend.Bots.Models;
using BotManager.Backend.Bots.Services.Implementations;
using Discord;
using Xunit;

namespace BotManager.Backend.Bots.Tests;

public class BoardComponentsBuilderTests
{
    private static BoardMessageDto CreateBoard(int teamCount, Func<int, string>? title = null, string? emoji = "🎯")
    {
        return new BoardMessageDto
        {
            Title = "Board",
            Entries = Enumerable.Range(1, teamCount)
                .Select(i => new BoardMessageEntryDto
                {
                    Title = title?.Invoke(i) ?? $"Team {i}",
                    Details = $"Details {i}",
                    Emoji = emoji
                })
                .ToList()
        };
    }

    private static List<SelectMenuComponent> GetSelectMenus(MessageComponent message)
        => message.Components
            .SelectMany(row => row.Components)
            .OfType<SelectMenuComponent>()
            .ToList();

    private static List<ButtonComponent> GetButtons(MessageComponent message)
        => message.Components
            .SelectMany(row => row.Components)
            .OfType<ButtonComponent>()
            .ToList();

    [Fact]
    public void BuildBoardComponents_WithNoTeams_RendersOnlyAdminButtons()
    {
        var message = BoardComponentsBuilder.BuildBoardComponents(CreateBoard(0)).Build();

        Assert.Empty(GetSelectMenus(message));
        Assert.Equal(
            new[] { BoardComponentsBuilder.BoardAddTeamActionId, BoardComponentsBuilder.BoardRefreshActionId },
            GetButtons(message).Select(b => b.CustomId).ToArray());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(25, 1)]
    [InlineData(26, 2)]
    [InlineData(50, 2)]
    [InlineData(51, 3)]
    [InlineData(100, 4)]
    public void BuildBoardComponents_SplitsTeamsIntoMenusOf25(int teamCount, int expectedMenus)
    {
        var message = BoardComponentsBuilder.BuildBoardComponents(CreateBoard(teamCount)).Build();
        var menus = GetSelectMenus(message);

        Assert.Equal(expectedMenus, menus.Count);
        Assert.All(menus, m => Assert.InRange(m.Options.Count, 1, BoardComponentsBuilder.MaxOptionsPerMenu));
        Assert.Equal(teamCount, menus.Sum(m => m.Options.Count));
        // Menus occupy the leading rows; admin buttons follow; Discord allows at most 5 rows.
        Assert.InRange(message.Components.Count, expectedMenus + 1, 5);
        var rows = message.Components.ToList();
        for (var i = 0; i < expectedMenus; i++)
        {
            Assert.IsType<SelectMenuComponent>(Assert.Single(rows[i].Components));
        }

        Assert.All(rows.Skip(expectedMenus), r => Assert.All(r.Components, c => Assert.IsType<ButtonComponent>(c)));
        Assert.Equal(2, GetButtons(message).Count);

        for (var i = 0; i < menus.Count; i++)
        {
            Assert.Equal($"{BoardComponentsBuilder.TeamSelectPrefix}{i}", menus[i].CustomId);
            Assert.Equal($"Choose your team ({i + 1}/{expectedMenus})", menus[i].Placeholder);
            Assert.Equal(1, menus[i].MinValues);
            Assert.Equal(1, menus[i].MaxValues);
        }
    }

    [Fact]
    public void BuildBoardComponents_PreservesTeamOrderAcrossMenus()
    {
        var message = BoardComponentsBuilder.BuildBoardComponents(CreateBoard(30)).Build();
        var values = GetSelectMenus(message).SelectMany(m => m.Options).Select(o => o.Value).ToList();

        Assert.Equal(Enumerable.Range(1, 30).Select(i => $"Team {i}"), values);
    }

    [Fact]
    public void BuildBoardComponents_CapsTeamsAt100()
    {
        var message = BoardComponentsBuilder.BuildBoardComponents(CreateBoard(130)).Build();
        var menus = GetSelectMenus(message);

        Assert.Equal(BoardComponentsBuilder.MaxTeamMenus, menus.Count);
        Assert.Equal(BoardComponentsBuilder.MaxTeamsTotal, menus.Sum(m => m.Options.Count));
        Assert.Equal("Team 100", menus.Last().Options.Last().Value);
        // Discord allows at most 5 action rows: 4 menus + admin row.
        Assert.Equal(5, message.Components.Count);
    }

    [Fact]
    public void BuildBoardComponents_WithFullBoard_PutsBothAdminButtonsOnRow4()
    {
        var message = BoardComponentsBuilder.BuildBoardComponents(CreateBoard(100)).Build();
        var lastRow = message.Components.Last();

        var buttons = lastRow.Components.OfType<ButtonComponent>().ToList();
        Assert.Equal(2, buttons.Count);
        Assert.Equal(BoardComponentsBuilder.BoardAddTeamActionId, buttons[0].CustomId);
        Assert.Equal(ButtonStyle.Success, buttons[0].Style);
        Assert.Equal(BoardComponentsBuilder.BoardRefreshActionId, buttons[1].CustomId);
        Assert.Equal(ButtonStyle.Secondary, buttons[1].Style);
        Assert.All(buttons, b => Assert.StartsWith(BoardComponentsBuilder.BoardActionPrefix, b.CustomId));
    }

    [Fact]
    public void BuildBoardComponents_TruncatesLongTeamNamesTo100Characters()
    {
        var longName = new string('x', 150);
        var message = BoardComponentsBuilder.BuildBoardComponents(CreateBoard(1, _ => longName)).Build();
        var option = GetSelectMenus(message).Single().Options.Single();

        Assert.Equal(100, option.Label.Length);
        Assert.Equal(100, option.Value.Length);
    }

    [Fact]
    public void BuildBoardComponents_SetsEmojiOnlyWhenParsable()
    {
        var board = CreateBoard(3);
        board.Entries[1].Emoji = "not-an-emoji";
        board.Entries[2].Emoji = "  ";

        var options = GetSelectMenus(BoardComponentsBuilder.BuildBoardComponents(board).Build()).Single().Options.ToList();

        Assert.Equal("🎯", options[0].Emote?.Name);
        Assert.Null(options[1].Emote);
        Assert.Null(options[2].Emote);
    }

    [Fact]
    public void CustomIdConstants_MatchPrefixes()
    {
        Assert.StartsWith(BoardComponentsBuilder.BoardActionPrefix, BoardComponentsBuilder.BoardAddTeamActionId);
        Assert.StartsWith(BoardComponentsBuilder.BoardActionPrefix, BoardComponentsBuilder.BoardRefreshActionId);
        Assert.StartsWith(BoardComponentsBuilder.BoardModalPrefix, BoardComponentsBuilder.BoardAddTeamModalId);
        // Plugin handlers (ReactionsCommandHandler) match these literal prefixes.
        Assert.Equal("team_toggle:", BoardComponentsBuilder.TeamTogglePrefix);
        Assert.Equal("team_select:", BoardComponentsBuilder.TeamSelectPrefix);
    }

    [Fact]
    public void BuildAddTeamModal_UsesModalIdAndExpectedInputs()
    {
        var modal = BoardComponentsBuilder.BuildAddTeamModal();

        Assert.Equal(BoardComponentsBuilder.BoardAddTeamModalId, modal.CustomId);
        var inputs = modal.Component.Components
            .SelectMany(row => row.Components)
            .OfType<TextInputComponent>()
            .ToList();

        // Plugin handlers (TeamsCommandHandler) read these ids.
        Assert.Equal(new[] { "team_name", "leader_name", "contact_info" }, inputs.Select(i => i.CustomId).ToArray());
        Assert.True(inputs[0].Required);
        Assert.True(inputs[1].Required);
        Assert.False(inputs[2].Required);
    }

    [Fact]
    public void BuildBoardEmbed_UsesDefaultTitleAndOneFieldPerEntry()
    {
        var board = CreateBoard(3);
        board.Title = " ";
        board.Description = "Desc";
        board.Entries[2].Details = "";

        var embed = BoardComponentsBuilder.BuildBoardEmbed(board).Build();

        Assert.Equal("📋 Seznam položek", embed.Title);
        Assert.Equal("Desc", embed.Description);
        Assert.Equal(3, embed.Fields.Length);
        Assert.Equal("🎯 Team 1", embed.Fields[0].Name);
        Assert.Equal("Details 1\n​", embed.Fields[0].Value);
        Assert.Equal("​\n​", embed.Fields[2].Value);
    }
}
