using BotManager.Backend.API.BotPlugins;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BotManager.Backend.API.Tests;

public class PluginRegistryTests
{
    [Fact]
    public async Task GetOrCreatePlugin_ConcurrentCalls_ReturnSingleInstancePerBot()
    {
        var sut = new PluginRegistry(NullLogger<PluginRegistry>.Instance);

        var plugins = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => sut.GetOrCreatePlugin(7, "discord-board"))));

        Assert.All(plugins, plugin => Assert.Same(plugins[0], plugin));
        Assert.Same(plugins[0], sut.GetPlugin(7));
    }

    [Fact]
    public void RemovePlugin_NextCallCreatesFreshInstance()
    {
        var sut = new PluginRegistry(NullLogger<PluginRegistry>.Instance);
        var first = sut.GetOrCreatePlugin(1, "discord-board");

        sut.RemovePlugin(1);

        Assert.Null(sut.GetPlugin(1));
        Assert.NotSame(first, sut.GetOrCreatePlugin(1, "discord-board"));
    }

    [Fact]
    public void GetOrCreatePlugin_UnknownPlugin_ThrowsAndIsNotCached()
    {
        var sut = new PluginRegistry(NullLogger<PluginRegistry>.Instance);

        Assert.Throws<KeyNotFoundException>(() => sut.GetOrCreatePlugin(2, "missing"));
        Assert.NotNull(sut.GetOrCreatePlugin(2, "discord-board"));
    }
}
