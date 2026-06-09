using BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Services;
using Xunit;

namespace BotManager.Backend.API.Tests;

public class BoardCommandDispatchPolicyTests
{
    [Fact]
    public void ResolveSubcommandName_ReturnsFirstNonEmptyName()
    {
        var result = BoardCommandDispatchPolicy.ResolveSubcommandName(["", "insert", "refresh"]);

        Assert.Equal("insert", result);
    }

    [Fact]
    public void ResolveSubcommandName_AllEmpty_ReturnsNull()
    {
        var result = BoardCommandDispatchPolicy.ResolveSubcommandName([null, "", "   "]);

        Assert.Null(result);
    }

    [Fact]
    public void ShouldDenyForAdmin_RequiresAdminAndUserNotAdmin_ReturnsTrue()
    {
        var deny = BoardCommandDispatchPolicy.ShouldDenyForAdmin(requiresAdmin: true, isAdministrator: false);

        Assert.True(deny);
    }

    [Fact]
    public void ShouldDenyForAdmin_RequiresAdminAndUserIsAdmin_ReturnsFalse()
    {
        var deny = BoardCommandDispatchPolicy.ShouldDenyForAdmin(requiresAdmin: true, isAdministrator: true);

        Assert.False(deny);
    }

    [Fact]
    public void ShouldDenyForAdmin_NoAdminRequirement_ReturnsFalse()
    {
        var deny = BoardCommandDispatchPolicy.ShouldDenyForAdmin(requiresAdmin: false, isAdministrator: false);

        Assert.False(deny);
    }
}
