using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Entities.Entities;
using Discord;
using Xunit;

namespace BotManager.Backend.Bots.Tests;

public class GatewayDisconnectPolicyTests
{
    [Fact]
    public void ShouldMarkOffline_ReturnsFalse_WhenGenerationWasSuperseded()
    {
        var shouldMark = GatewayDisconnectPolicy.ShouldMarkOffline(
            disconnectGeneration: 5,
            currentGeneration: 6,
            connectionState: ConnectionState.Disconnected,
            persistedStatus: BotStatus.Reconnecting);

        Assert.False(shouldMark);
    }

    [Fact]
    public void ShouldMarkOffline_ReturnsFalse_WhenGatewayIsAlreadyConnected()
    {
        var shouldMark = GatewayDisconnectPolicy.ShouldMarkOffline(
            disconnectGeneration: 5,
            currentGeneration: 5,
            connectionState: ConnectionState.Connected,
            persistedStatus: BotStatus.Reconnecting);

        Assert.False(shouldMark);
    }

    [Fact]
    public void ShouldMarkOffline_ReturnsFalse_WhenPersistedStatusWasRestoredToOnline()
    {
        var shouldMark = GatewayDisconnectPolicy.ShouldMarkOffline(
            disconnectGeneration: 5,
            currentGeneration: 5,
            connectionState: ConnectionState.Disconnected,
            persistedStatus: BotStatus.Online);

        Assert.False(shouldMark);
    }

    [Fact]
    public void ShouldMarkOffline_ReturnsTrue_WhenStillDisconnectedAndNotSuperseded()
    {
        var shouldMark = GatewayDisconnectPolicy.ShouldMarkOffline(
            disconnectGeneration: 5,
            currentGeneration: 5,
            connectionState: ConnectionState.Disconnected,
            persistedStatus: BotStatus.Reconnecting);

        Assert.True(shouldMark);
    }
}
