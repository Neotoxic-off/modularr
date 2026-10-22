using Modularr.Core.Persistence;

namespace Modularr.Core.Activities;

public enum ActivityKind
{
    Added,
    SearchStarted,
    SearchCompleted,
    NothingFound,
    SearchFailed,
    Grabbed,
    DownloadStarted,
    DownloadStalled,
    DownloadFailed,
    Blocklisted,
    Regrabbed,
    Imported,
    ImportFailed,
    Removed,
    ScanCompleted,
}

public enum ActivityLevel
{
    Info,
    Success,
    Warning,
    Error,
}

// One line of the notification feed: Title names the item or release, Message says what happened to it.
// Not tied to the series or movie rows, so the feed keeps what happened to an item after it is removed.
public sealed class Activity
{
    public int Id { get; set; }
    public DateTime Time { get; set; }
    public ActivityKind Kind { get; set; }
    public ActivityLevel Level { get; set; }
    public required string Title { get; set; }
    public string? Message { get; set; }
    public int? SeriesId { get; set; }
    public int? MovieId { get; set; }
    public int? DownloadId { get; set; }

    // shortcut: read is shared by every user, keep a per-user read mark if several people use the feed.
    public bool Read { get; set; }

    public static Activity For(TrackedDownload download, ActivityKind kind, ActivityLevel level, string? message)
    {
        ArgumentNullException.ThrowIfNull(download);
        return new Activity
        {
            Kind = kind,
            Level = level,
            Title = download.ReleaseTitle,
            Message = message,
            SeriesId = download.SeriesId,
            MovieId = download.MovieId,
            DownloadId = download.Id,
        };
    }
}
