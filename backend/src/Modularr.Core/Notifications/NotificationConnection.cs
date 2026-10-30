using System.Text.Json.Serialization;

namespace Modularr.Core.Notifications;

public enum NotificationType
{
    Webhook,
    Discord,
    Plex,
    Jellyfin,
    Emby,
}

// One entry of the modularr.json "Notifications" list, edited through PUT /api/settings/notifications.
// Plex, Jellyfin and Emby only refresh their libraries on import; the others post every event they are on for.
public sealed record NotificationConnection
{
    public string Name { get; init; } = "";
    public NotificationType Type { get; init; }
    public string Url { get; init; } = "";
    public string? Token { get; init; }

    // A Webhook sends Token in this header when set (linkarr's managed hook only; not part of the saved settings).
    [JsonIgnore]
    public string? TokenHeader { get; init; }

    public bool OnGrab { get; init; } = true;
    public bool OnImport { get; init; } = true;
    public bool OnFailure { get; init; } = true;

    public bool IsMediaServer => Type is NotificationType.Plex or NotificationType.Jellyfin or NotificationType.Emby;

    public bool Wants(string notificationEvent) => notificationEvent switch
    {
        NotificationEvent.Import => OnImport,
        NotificationEvent.Grab => OnGrab && !IsMediaServer,
        NotificationEvent.Failure => OnFailure && !IsMediaServer,
        _ => notificationEvent == NotificationEvent.Test,
    };

    // Admin-chosen targets may be on the LAN (media servers usually are), so only the scheme is restricted.
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 100)
        {
            throw new ArgumentException("A notification connection needs a name of at most 100 characters.", nameof(Name));
        }

        if (!Enum.IsDefined(Type))
        {
            throw new ArgumentException($"{Name}: unknown type '{Type}'.", nameof(Type));
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
        {
            throw new ArgumentException($"{Name}: the URL must be an absolute http or https address.", nameof(Url));
        }

        if (IsMediaServer && string.IsNullOrWhiteSpace(Token))
        {
            throw new ArgumentException($"{Name}: {Type} needs its API token.", nameof(Token));
        }
    }

    public NotificationConnection Redacted() => this with { Url = "", Token = null };
}

public static class NotificationEvent
{
    public const string Grab = "grab";
    public const string Import = "import";
    public const string Failure = "failure";
    public const string Test = "test";
}
