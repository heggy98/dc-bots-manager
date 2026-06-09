using BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Handlers;
using Xunit;

namespace BotManager.Backend.API.Tests;

public class BoardInteractionResponsePolicyTests
{
    [Fact]
    public void ShouldUseFollowup_WhenAlreadyResponded_ReturnsTrue()
    {
        var result = BoardInteractionResponsePolicy.ShouldUseFollowup(hasResponded: true, interactionAgeMs: 10);

        Assert.True(result);
    }

    [Fact]
    public void ShouldUseFollowup_WhenNotRespondedAndFreshInteraction_ReturnsFalse()
    {
        var result = BoardInteractionResponsePolicy.ShouldUseFollowup(hasResponded: false, interactionAgeMs: 1000);

        Assert.False(result);
    }

    [Fact]
    public void ShouldUseFollowup_WhenNotRespondedButNearDeadline_ReturnsTrue()
    {
        var result = BoardInteractionResponsePolicy.ShouldUseFollowup(hasResponded: false, interactionAgeMs: 2500);

        Assert.True(result);
    }

    [Fact]
    public void ShouldAttemptLateDefer_WhenAlreadyResponded_ReturnsFalse()
    {
        var result = BoardInteractionResponsePolicy.ShouldAttemptLateDefer(hasResponded: true);

        Assert.False(result);
    }

    [Fact]
    public void ShouldAttemptLateDefer_WhenNotResponded_ReturnsTrue()
    {
        var result = BoardInteractionResponsePolicy.ShouldAttemptLateDefer(hasResponded: false);

        Assert.True(result);
    }

    [Theory]
    [InlineData(40060)]
    public void IsAlreadyAcknowledgedError_WhenCodeMatches_ReturnsTrue(int code)
    {
        var result = BoardInteractionResponsePolicy.IsAlreadyAcknowledgedError(code);

        Assert.True(result);
    }

    [Theory]
    [InlineData(10015)]
    [InlineData(10062)]
    public void IsExpiredInteractionError_WhenCodeMatches_ReturnsTrue(int code)
    {
        var result = BoardInteractionResponsePolicy.IsExpiredInteractionError(code);

        Assert.True(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(50013)]
    [InlineData(403)]
    public void IsExpiredInteractionError_WhenCodeDoesNotMatch_ReturnsFalse(int? code)
    {
        var result = BoardInteractionResponsePolicy.IsExpiredInteractionError(code);

        Assert.False(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(10015)]
    [InlineData(10062)]
    [InlineData(50013)]
    public void IsAlreadyAcknowledgedError_WhenCodeDoesNotMatch_ReturnsFalse(int? code)
    {
        var result = BoardInteractionResponsePolicy.IsAlreadyAcknowledgedError(code);

        Assert.False(result);
    }
}
