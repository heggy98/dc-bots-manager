using System.Net;
using System.Text.Json;
using BotManager.Backend.Bots.Models;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Services.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BotManager.Backend.Bots.Tests;

public class BotAlertServiceTests
{
    private const string WebhookUrl = "https://discord.com/api/webhooks/123/secret-webhook-token";

    [Fact]
    public async Task NotifyAsync_Offline_PostsEmbedToWebhook()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler, new ManualTimeProvider());

        await service.NotifyAsync(new BotAlert(7, "Board @everyone", BotAlertKind.Offline, "Gateway reconnect failed"));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(WebhookUrl, request.Uri);

        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;
        Assert.Empty(root.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray());
        var embed = Assert.Single(root.GetProperty("embeds").EnumerateArray());
        Assert.Equal("Bot \"Board @everyone\" is offline", embed.GetProperty("title").GetString());
        Assert.Equal("Gateway reconnect failed", embed.GetProperty("description").GetString());
        Assert.Contains(embed.GetProperty("fields").EnumerateArray(), f => f.GetProperty("value").GetString() == "7");
    }

    [Fact]
    public async Task NotifyAsync_SameBotWithinWindow_IsDebounced()
    {
        var handler = new RecordingHandler();
        var time = new ManualTimeProvider();
        var service = CreateService(handler, time);

        await service.NotifyAsync(new BotAlert(1, "A", BotAlertKind.Offline));
        time.Advance(TimeSpan.FromMinutes(5));
        await service.NotifyAsync(new BotAlert(1, "A", BotAlertKind.StartFailed));
        await service.NotifyAsync(new BotAlert(2, "B", BotAlertKind.Offline));
        Assert.Equal(2, handler.Requests.Count);

        time.Advance(TimeSpan.FromMinutes(6));
        await service.NotifyAsync(new BotAlert(1, "A", BotAlertKind.Offline));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task NotifyAsync_Recovered_OnlyAfterDeliveredDownAlert()
    {
        var handler = new RecordingHandler();
        var time = new ManualTimeProvider();
        var service = CreateService(handler, time);

        // No outstanding outage: nothing to recover from.
        await service.NotifyAsync(new BotAlert(1, "A", BotAlertKind.Recovered));
        Assert.Empty(handler.Requests);

        await service.NotifyAsync(new BotAlert(1, "A", BotAlertKind.Offline));
        await service.NotifyAsync(new BotAlert(1, "A", BotAlertKind.Recovered));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("back online", handler.Requests[1].Body);

        // Flapping within the window: the down alert is suppressed, so is the following recovery.
        time.Advance(TimeSpan.FromMinutes(1));
        await service.NotifyAsync(new BotAlert(1, "A", BotAlertKind.Offline));
        await service.NotifyAsync(new BotAlert(1, "A", BotAlertKind.Recovered));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task NotifyAsync_WebhookFailure_DoesNotThrow()
    {
        var handler = new RecordingHandler { Status = HttpStatusCode.InternalServerError };
        var service = CreateService(handler, new ManualTimeProvider());

        await service.NotifyAsync(new BotAlert(1, "A", BotAlertKind.Offline));

        handler.Throw = true;
        await service.NotifyAsync(new BotAlert(2, "B", BotAlertKind.Offline));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task NotifyAsync_NoChannelConfigured_SendsNothing()
    {
        var handler = new RecordingHandler();
        var service = new BotAlertService(new FakeHttpClientFactory(handler), Options.Create(new BotAlertOptions()),
            NullLogger<BotAlertService>.Instance, new ManualTimeProvider());

        Assert.False(service.IsEnabled);
        await service.NotifyAsync(new BotAlert(1, "A", BotAlertKind.Offline));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NotifyAsync_RedactsTokensInReason()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler, new ManualTimeProvider());
        // Assembled at runtime so secret scanners do not flag this obviously fake token.
        var fakeToken = string.Join(".", "MTIzNDU2Nzg5MDEyMzQ1Njc4OQ", "GaBcDe", "abcdefghijklmnopqrstuvwxyz0123");

        await service.NotifyAsync(new BotAlert(1, "A", BotAlertKind.StartFailed, $"Login failed for {fakeToken} via {WebhookUrl}"));

        var body = Assert.Single(handler.Requests).Body;
        Assert.DoesNotContain(fakeToken, body);
        Assert.DoesNotContain("secret-webhook-token", body);
        Assert.Contains("[redacted-token]", body);
    }

    private static BotAlertService CreateService(RecordingHandler handler, TimeProvider time)
        => new(
            new FakeHttpClientFactory(handler),
            Options.Create(new BotAlertOptions { DiscordWebhookUrl = WebhookUrl, DebounceMinutes = 10 }),
            NullLogger<BotAlertService>.Instance,
            time);

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.NoContent;
        public bool Throw { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.ToString(), body));
            if (Throw)
            {
                throw new HttpRequestException("connection refused");
            }

            return new HttpResponseMessage(Status);
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
