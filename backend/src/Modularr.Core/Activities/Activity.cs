using System.Text.Json.Serialization;
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
    SubtitleDownloaded,
    SubtitlesMissing,
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

    // Not stored: filled for the reader from their own read mark (User.ReadActivityUpTo).
    public bool Read { get; set; }

    // Not stored nor sent: the imported title's folder, so a media server can refresh just that path.
    [JsonIgnore]
    public string? Path { get; set; }

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
