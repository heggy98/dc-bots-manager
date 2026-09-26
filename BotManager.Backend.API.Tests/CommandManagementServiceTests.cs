using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BotManager.Backend.API.Tests;

public class CommandManagementServiceTests
{
    [Fact]
    public async Task RegisterCommandAsync_ExistingCommand_KeepsAdminCustomizations()
    {
        var options = new DbContextOptionsBuilder<BotManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new BotManagerDbContext(options);
        db.Bots.Add(new Bot { BotId = 1, Name = "b", BotToken = "t", OwnerUserId = "o" });
        await db.SaveChangesAsync();

        var sut = new CommandManagementService(db, NullLogger<CommandManagementService>.Instance,
            new NoopNotificationService(), new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

        await sut.RegisterCommandAsync(1, "board", "add-team", "Default description", successMessage: "Default ok");

        var command = await db.BotCommands.SingleAsync();
        command.Description = "Custom description";
        command.SuccessMessage = "Custom ok";
        command.MinimumPermissionLevel = 2;
        command.IsEnabled = false;
        await db.SaveChangesAsync();

        // Simulates the registration that runs on every bot start.
        await sut.RegisterCommandAsync(1, "board", "add-team", "Default description", successMessage: "Default ok");

        var reloaded = await db.BotCommands.AsNoTracking().SingleAsync();
        Assert.Equal("Custom description", reloaded.Description);
        Assert.Equal("Custom ok", reloaded.SuccessMessage);
        Assert.Equal(2, reloaded.MinimumPermissionLevel);
        Assert.False(reloaded.IsEnabled);
    }

    private sealed class NoopNotificationService : IBotNotificationService
    {
        public Task NotifyBotStatusChangedAsync(int botId, BotStatus status) => Task.CompletedTask;
        public Task NotifyNewLogAsync(int botId, string level, string message, DateTime timestamp) => Task.CompletedTask;
        public Task NotifyStatsUpdatedAsync(int botId, int requests24h, int errors24h) => Task.CompletedTask;
        public Task NotifyHistoryUpdatedAsync(int botId) => Task.CompletedTask;
    }
}
