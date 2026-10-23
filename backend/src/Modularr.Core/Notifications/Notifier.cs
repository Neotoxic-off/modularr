using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;

namespace Modularr.Core.Notifications;

// Path: the imported title's folder, as modularr sees it (both apps mount the media at /media in Docker, so the media server sees it alike).
public sealed record NotificationMessage(string Event, string Title, string? Message, int? SeriesId, int? MovieId, DateTime Time, string? Path = null);

// Sends one message to one connection; never throws for a failed send, it returns the error text (without the token or URL).
public sealed class NotificationSender(HttpClient http)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // Jellyfin and Emby read PascalCase bodies.
    private static readonly JsonSerializerOptions PascalCase = new();

    public async Task<string?> SendAsync(NotificationConnection connection, NotificationMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try
        {
            using HttpRequestMessage request = BuildRequest(connection, message);
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"No answer within {Timeout.TotalSeconds:0} s.";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or UriFormatException)
        {
            return Scrub(ex.Message, connection);
        }
    }

    public static HttpRequestMessage BuildRequest(NotificationConnection connection, NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(message);
        string url = connection.Url.TrimEnd('/');
        HttpRequestMessage request = connection.Type switch
        {
            NotificationType.Webhook => new(HttpMethod.Post, connection.Url)
            {
                Content = JsonContent.Create(new { message.Event, message.Title, message.Message, message.SeriesId, message.MovieId, message.Time }),
            },
            NotificationType.Discord => new(HttpMethod.Post, connection.Url) { Content = JsonContent.Create(new { Content = DiscordText(message) }) },
            NotificationType.Plex => new(HttpMethod.Get, url + "/library/sections/all/refresh"),
            // A known folder refreshes only that path; without one (a test), the whole library.
            _ when !string.IsNullOrEmpty(message.Path) => new(HttpMethod.Post, url + "/Library/Media/Updated")
            {
                Content = JsonContent.Create(new { Updates = new[] { new { message.Path, UpdateType = "Created" } } }, options: PascalCase),
            },
            _ => new(HttpMethod.Post, url + "/Library/Refresh"),
        };
        if (connection.IsMediaServer)
        {
            request.Headers.Add(connection.Type == NotificationType.Plex ? "X-Plex-Token" : "X-Emby-Token", connection.Token);
        }
        else if (connection.Type == NotificationType.Webhook && !string.IsNullOrEmpty(connection.TokenHeader) && !string.IsNullOrEmpty(connection.Token))
        {
            request.Headers.Add(connection.TokenHeader, connection.Token);
        }

        return request;
    }

    public static string DiscordText(NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return string.IsNullOrEmpty(message.Message) ? $"{message.Event}: {message.Title}" : $"{message.Event}: {message.Title} — {message.Message}";
    }

    // A Discord webhook carries its secret in the URL, so neither the URL nor the token may reach the UI or the log.
    public static string Scrub(string text, NotificationConnection connection)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(connection);
        foreach (string secret in new[] { connection.Url, connection.Url.TrimEnd('/'), connection.Token }.OfType<string>().Where(s => s.Length > 0))
        {
            text = text.Replace(secret, "***", StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }
}

// Forwards grab, import and failure activities to the configured connections and linkarr's hook, one send at a time. The feed subscription is
// the bounded queue: a slow target makes it drop its oldest events, never blocks the publisher.
public sealed partial class NotificationService(ActivityFeed feed, NotificationSender sender, IOptionsMonitor<ModularrOptions> options, ILogger<NotificationService> logger)
    : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    public static string? EventFor(ActivityKind kind) => kind switch
    {
        ActivityKind.Grabbed => NotificationEvent.Grab,
        ActivityKind.Imported => NotificationEvent.Import,
        ActivityKind.DownloadFailed or ActivityKind.ImportFailed => NotificationEvent.Failure,
        _ => null,
    };

    // The configured connections, plus linkarr's hook when it sent a token.
    public static IReadOnlyList<NotificationConnection> Connections(ModularrOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Managed?.Hook() is NotificationConnection hook ? [.. options.Notifications, hook] : options.Notifications;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subscribed only while a connection exists: any subscriber keeps the progress ticker running.
        using PeriodicTimer timer = new(CheckInterval);
        do
        {
            if (Connections(options.CurrentValue).Count == 0)
            {
                continue;
            }

            await foreach (ServerEvent? serverEvent in feed.SubscribeAsync(CheckInterval, stoppingToken).ConfigureAwait(false))
            {
                IReadOnlyList<NotificationConnection> connections = Connections(options.CurrentValue);
                if (connections.Count == 0)
                {
                    break;
                }

                if (serverEvent?.Data is Activity activity && EventFor(activity.Kind) is string notificationEvent)
                {
                    NotificationMessage message = new(notificationEvent, activity.Title, activity.Message, activity.SeriesId, activity.MovieId, activity.Time, activity.Path);
                    foreach (NotificationConnection connection in connections.Where(c => c.Wants(notificationEvent)))
                    {
                        if (await sender.SendAsync(connection, message, stoppingToken).ConfigureAwait(false) is string error)
                        {
                            LogSendFailed(logger, connection.Name, notificationEvent, error);
                        }
                    }
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification {Name} ({Event}) failed: {Error}")]
    private static partial void LogSendFailed(ILogger logger, string name, string @event, string error);
}
