using System.Text.Json.Serialization;
using Modularr.Core.Notifications;

namespace Modularr.Core.Configuration;

// The modularr.json "Managed" section, set by linkarr's provisioning: the hub that owns the accounts here. Absent: managed locally.
public sealed record ManagedOptions
{
    public string? Name { get; init; }

    public string? Url { get; init; }

    // linkarr's secret for its event hook. Bound from the file but never serialized, so no API answer carries it.
    [JsonIgnore]
    public string? HookToken { get; init; }

    // Where modularr itself reaches linkarr (in Docker, http://linkarr:8070): Url is the browsers' address. Url when blank.
    [JsonIgnore]
    public string? HookUrl { get; init; }

    // With a hook token, linkarr gets the grab, import and failure events like a Webhook notification. The token is the
    // connection's Token too, so NotificationSender.Scrub keeps it out of error texts and the log. It travels in a header,
    // never in the URL (which proxies and access logs record).
    public const string HookTokenHeader = "X-Linkarr-Token";

    public NotificationConnection? Hook() => string.IsNullOrEmpty(HookToken) ? null : new NotificationConnection
    {
        Name = Name ?? "linkarr",
        Type = NotificationType.Webhook,
        Url = $"{(string.IsNullOrWhiteSpace(HookUrl) ? Url : HookUrl)?.TrimEnd('/')}/api/hooks/modularr",
        Token = HookToken,
        TokenHeader = HookTokenHeader,
    };

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 100)
        {
            throw new ArgumentException("The name must be 1 to 100 characters.", nameof(Name));
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
        {
            throw new ArgumentException("The URL must be an absolute http or https address.", nameof(Url));
        }

        if (!string.IsNullOrWhiteSpace(HookUrl)
            && (!Uri.TryCreate(HookUrl, UriKind.Absolute, out Uri? hook) || hook.Scheme is not ("http" or "https") || string.IsNullOrEmpty(hook.Host)))
        {
            throw new ArgumentException("The hook URL must be an absolute http or https address.", nameof(HookUrl));
        }

        if (HookToken is not null && (string.IsNullOrWhiteSpace(HookToken) || HookToken.Length > 512))
        {
            throw new ArgumentException("The hook token must be 1 to 512 characters.", nameof(HookToken));
        }
    }
}
