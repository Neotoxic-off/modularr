using System.Net;
using System.Text.Json.Nodes;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Notifications;

namespace Modularr.Core.Tests.Notifications;

public sealed class NotificationTests
{
    private static readonly NotificationMessage Message = new("import", "Show S01E01", "Imported", 4, null, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

    private static NotificationConnection Connection(NotificationType type, string url = "http://10.0.0.2:8096/") =>
        new() { Name = "Target", Type = type, Url = url, Token = "tok3n" };

    private static async Task<JsonObject> Body(HttpRequestMessage request) =>
        JsonNode.Parse(await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();

    [Fact]
    public async Task FormatsEachTypesRequest()
    {
        using HttpRequestMessage webhook = NotificationSender.BuildRequest(Connection(NotificationType.Webhook, "https://example.com/hook"), Message);
        JsonObject body = await Body(webhook);
        Assert.Equal((HttpMethod.Post, "https://example.com/hook"), (webhook.Method, webhook.RequestUri!.ToString()));
        Assert.Equal(["event", "title", "message", "seriesId", "movieId", "time"], body.Select(p => p.Key));
        Assert.Equal(("import", 4), ((string)body["event"]!, (int)body["seriesId"]!));

        using HttpRequestMessage discord = NotificationSender.BuildRequest(Connection(NotificationType.Discord, "https://discord.com/api/webhooks/1/x"), Message);
        Assert.Equal("import: Show S01E01 — Imported", (string)(await Body(discord))["content"]!);

        using HttpRequestMessage plex = NotificationSender.BuildRequest(Connection(NotificationType.Plex), Message);
        Assert.Equal((HttpMethod.Get, "http://10.0.0.2:8096/library/sections/all/refresh"), (plex.Method, plex.RequestUri!.ToString()));
        Assert.Equal("tok3n", plex.Headers.GetValues("X-Plex-Token").Single());

        using HttpRequestMessage jellyfin = NotificationSender.BuildRequest(Connection(NotificationType.Jellyfin), Message);
        Assert.Equal((HttpMethod.Post, "http://10.0.0.2:8096/Library/Refresh"), (jellyfin.Method, jellyfin.RequestUri!.ToString()));
        Assert.Equal("tok3n", jellyfin.Headers.GetValues("X-Emby-Token").Single());
    }

    [Fact]
    public async Task JellyfinAndEmbyRefreshOnlyTheImportedFolder()
    {
        NotificationMessage imported = Message with { Path = "/media/tv/Show" };
        using HttpRequestMessage emby = NotificationSender.BuildRequest(Connection(NotificationType.Emby), imported);
        Assert.Equal((HttpMethod.Post, "http://10.0.0.2:8096/Library/Media/Updated"), (emby.Method, emby.RequestUri!.ToString()));
        JsonObject update = (await Body(emby))["Updates"]!.AsArray().Single()!.AsObject();
        Assert.Equal(("/media/tv/Show", "Created"), ((string)update["Path"]!, (string)update["UpdateType"]!));
        Assert.Equal("tok3n", emby.Headers.GetValues("X-Emby-Token").Single());

        using HttpRequestMessage plex = NotificationSender.BuildRequest(Connection(NotificationType.Plex), imported);
        Assert.Equal("http://10.0.0.2:8096/library/sections/all/refresh", plex.RequestUri!.ToString());
    }

    [Fact]
    public async Task ErrorTextNeverCarriesTheTokenOrUrl()
    {
        NotificationConnection discord = Connection(NotificationType.Discord, "https://discord.com/api/webhooks/1/tok3n");
        using HttpClient failing = new(new StubHandler(_ => throw new HttpRequestException($"Failed to reach {discord.Url} with tok3n")));
        string? error = await new NotificationSender(failing).SendAsync(discord, Message, TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.DoesNotContain("tok3n", error, StringComparison.OrdinalIgnoreCase);

        using HttpClient refusing = new(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        Assert.Equal("HTTP 401 Unauthorized", await new NotificationSender(refusing).SendAsync(Connection(NotificationType.Plex), Message, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("http://192.168.1.5:32400", true)]
    [InlineData("https://discord.com/api/webhooks/1/x", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("/relative", false)]
    [InlineData("", false)]
    public void AcceptsOnlyHttpUrls(string url, bool valid)
    {
        Exception? error = Record.Exception(() => Connection(NotificationType.Webhook, url).EnsureValid());
        Assert.Equal(valid, error is null);
    }

    [Fact]
    public void MediaServersOnlyRefreshOnImport()
    {
        Assert.Equal(NotificationEvent.Grab, NotificationService.EventFor(ActivityKind.Grabbed));
        Assert.Equal(NotificationEvent.Failure, NotificationService.EventFor(ActivityKind.ImportFailed));
        Assert.Null(NotificationService.EventFor(ActivityKind.SearchStarted));
        Assert.False(Connection(NotificationType.Plex).Wants(NotificationEvent.Grab));
        Assert.True(Connection(NotificationType.Emby).Wants(NotificationEvent.Import));
        Assert.True(Connection(NotificationType.Discord).Wants(NotificationEvent.Failure));
        Assert.False((Connection(NotificationType.Discord) with { OnImport = false }).Wants(NotificationEvent.Import));
    }

    [Fact]
    public async Task LinkarrsHookGetsEveryEventLikeAWebhookWithoutLeakingItsToken()
    {
        const string Token = "h00k+tok/en";
        ModularrOptions options = new()
        {
            Notifications = [Connection(NotificationType.Discord, "https://discord.com/api/webhooks/1/x")],
            Managed = new ManagedOptions { Name = "linkarr", Url = "https://hub.example.com/", HookToken = Token },
        };

        NotificationConnection hook = NotificationService.Connections(options)[^1];
        Assert.Equal(2, NotificationService.Connections(options).Count);
        Assert.True(new[] { NotificationEvent.Grab, NotificationEvent.Import, NotificationEvent.Failure }.All(hook.Wants));
        using HttpRequestMessage request = NotificationSender.BuildRequest(hook, Message);
        Assert.Equal((HttpMethod.Post, "https://hub.example.com/api/hooks/modularr"), (request.Method, request.RequestUri!.AbsoluteUri));
        // The token travels in a header, never in the URL.
        Assert.Equal(Token, Assert.Single(request.Headers.GetValues(ManagedOptions.HookTokenHeader)));
        Assert.Equal(["event", "title", "message", "seriesId", "movieId", "time"], (await Body(request)).Select(p => p.Key));

        // The logged error text carries neither the token nor the URL that holds it.
        using HttpClient failing = new(new StubHandler(r => throw new HttpRequestException($"Failed: {r.RequestUri} ({Token})")));
        string? error = await new NotificationSender(failing).SendAsync(hook, Message, TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.DoesNotContain("h00k", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Token, System.Text.Json.JsonSerializer.Serialize(options.Managed), StringComparison.Ordinal);

        Assert.Single(NotificationService.Connections(new ModularrOptions { Notifications = options.Notifications, Managed = options.Managed with { HookToken = null } }));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
