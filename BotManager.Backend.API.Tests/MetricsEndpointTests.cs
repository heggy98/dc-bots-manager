using System.Net;
using System.Net.Http.Headers;
using BotManager.Backend.API.Services;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Services.Implementations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BotManager.Backend.API.Tests;

public class MetricsEndpointTests
{
    [Fact]
    public async Task Metrics_Disabled_EndpointNotMapped()
    {
        await using var app = await StartAsync(new Dictionary<string, string?>());
        using var client = CreateClient(app);

        var response = await client.GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Metrics_DirectRequest_ExposesCustomMeters()
    {
        await using var app = await StartAsync(new Dictionary<string, string?> { ["Metrics:Enabled"] = "true" });
        var metrics = app.Services.GetRequiredService<BotManagerMetrics>();
        metrics.RecordCommandExecuted("board", success: true);
        metrics.RecordDisconnect(3);
        metrics.RecordAlertSent(BotAlertKind.Offline, "discord", success: true);
        using var client = CreateClient(app);

        var body = await client.GetStringAsync("/metrics");

        Assert.Contains("commands_executed_total{", body);
        Assert.Contains("command=\"board\"", body);
        Assert.Contains("bot_disconnects_total{", body);
        Assert.Contains("alerts_sent_total{", body);
    }

    [Fact]
    public async Task Metrics_ProxiedRequest_IsRejected()
    {
        await using var app = await StartAsync(new Dictionary<string, string?> { ["Metrics:Enabled"] = "true" });
        using var client = CreateClient(app);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Metrics_ApiKeyConfigured_RequiresBearer()
    {
        await using var app = await StartAsync(new Dictionary<string, string?>
        {
            ["Metrics:Enabled"] = "true",
            ["Metrics:ApiKey"] = "scrape-secret"
        });
        using var client = CreateClient(app);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/metrics")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/metrics")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "scrape-secret");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/metrics")).StatusCode);
    }

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddBotManagerObservability();
        var app = builder.Build();
        app.UseForwardedHeaders();
        app.UseBotManagerMetricsEndpoint();
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new HttpClient { BaseAddress = new Uri(address) };
    }
}
