namespace Modularr.Core.Downloads.Torrents;

// Same values as Sonarr/Radarr DownloadItemStatus so the import pipeline keeps their semantics.
public enum DownloadItemStatus
{
    Queued = 0,
    Paused = 1,
    Downloading = 2,
    Completed = 3,
    Failed = 4,
    Warning = 5,
}
