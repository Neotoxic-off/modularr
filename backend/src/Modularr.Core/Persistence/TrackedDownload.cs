namespace Modularr.Core.Persistence;

// Same values as Sonarr/Radarr TrackedDownloadState.
public enum TrackedDownloadState
{
    Downloading = 0,
    ImportBlocked = 1,
    ImportPending = 2,
    Importing = 3,
    Imported = 4,
    FailedPending = 5,
    Failed = 6,
    Ignored = 7,
}

// A grabbed torrent and what it was grabbed for, so the import knows where it goes.
public sealed class TrackedDownload
{
    public int Id { get; set; }

    // Unique, compared case-insensitively.
    public required string InfoHash { get; set; }
    public int? SeriesId { get; set; }
    public IReadOnlyList<int> EpisodeIds { get; set; } = [];
    public int? MovieId { get; set; }
    public required string ReleaseTitle { get; set; }
    public string? Indexer { get; set; }
    public TrackedDownloadState State { get; set; }

    // Why it failed; kept here because the torrent, and its status message, is removed.
    public string? Message { get; set; }

    // Automatic re-grabs in a row that led to this download, capped by torrents.maxAutomaticRegrabs.
    public int Regrabs { get; set; }
}
