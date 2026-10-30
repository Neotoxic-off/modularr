using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;

namespace Modularr.Api;

// The notification feed: paged history, unread count, read marks, and the live Server-Sent Events stream.
public static class ActivityEndpoints
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    public static RouteGroupBuilder MapActivityApi(this RouteGroupBuilder api)
    {
        api.MapGet("/activity", (int? before, int? limit, int? seriesId, int? movieId, HttpContext context, ActivityLog activities, CancellationToken ct) =>
            activities.ListAsync(AuthEndpoints.UserId(context.User), before, limit ?? 50, seriesId, movieId, ct));
        api.MapGet("/activity/unread-count", async (HttpContext context, ActivityLog activities, CancellationToken ct) =>
            new { count = await activities.UnreadCountAsync(AuthEndpoints.UserId(context.User), ct) });
        api.MapPost("/activity/read", async (int? upTo, HttpContext context, ActivityLog activities, CancellationToken ct) =>
        {
            await activities.MarkReadAsync(AuthEndpoints.UserId(context.User), upTo, ct);
            return Results.NoContent();
        });
        api.MapGet("/events", StreamAsync);
        return api;
    }

    private static readonly TimeSpan StreamLifetime = TimeSpan.FromMinutes(5);

    // "event: activity|progress" frames with JSON data, and a comment every Heartbeat so proxies keep the connection open.
    private static async Task StreamAsync(
        HttpContext context, ActivityFeed feed, IOptions<JsonOptions> json, IHostApplicationLifetime lifetime, CancellationToken ct)
    {
        // Ends with the client or with the app, which would otherwise wait for every open stream on shutdown.
        using CancellationTokenSource stream = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.ApplicationStopping);
        // The cookie is only checked when a request starts: ending the stream regularly makes the browser reconnect and be
        // checked again, so a deleted, demoted or logged-out account stops receiving events within StreamLifetime.
        stream.CancelAfter(StreamLifetime);
        HttpResponse response = context.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        try
        {
            // Reconnect fast when the stream ends at StreamLifetime, so the UI does not see it as the server going away.
            await response.WriteAsync("retry: 1000\n\n", stream.Token);
            await response.Body.FlushAsync(stream.Token);
            await foreach (ServerEvent? serverEvent in feed.SubscribeAsync(Heartbeat, stream.Token))
            {
                await response.WriteAsync(
                    serverEvent is null
                        ? ": heartbeat\n\n"
                        : $"event: {serverEvent.Type}\ndata: {JsonSerializer.Serialize(serverEvent.Data, json.Value.SerializerOptions)}\n\n",
                    stream.Token);
                await response.Body.FlushAsync(stream.Token);
            }
        }
        catch (OperationCanceledException) when (stream.IsCancellationRequested)
        {
            // The client went away or the app is stopping.
        }
    }
}
