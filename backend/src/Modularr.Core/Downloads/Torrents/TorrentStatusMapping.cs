using MonoTorrent.Client;

namespace Modularr.Core.Downloads.Torrents;

// Port of the qBittorrent state mapping in Sonarr/Radarr QBittorrent.GetItems, applied to MonoTorrent states.
public static class TorrentStatusMapping
{
    // Seed ratios are floats upstream; a tiny gap still counts as reached.
    private const double RatioTolerance = 0.001;

    public static (DownloadItemStatus Status, string? Message) Map(TorrentState state, bool complete, bool dhtEnabled, string? error = null) => state switch
    {
        TorrentState.Downloading => (DownloadItemStatus.Downloading, null),
        TorrentState.Seeding => (DownloadItemStatus.Completed, null),
        TorrentState.Stopped or TorrentState.Stopping or TorrentState.Paused =>
            (complete ? DownloadItemStatus.Completed : DownloadItemStatus.Paused, null),
        TorrentState.Starting or TorrentState.Hashing or TorrentState.HashingPaused or TorrentState.FetchingHashes =>
            (DownloadItemStatus.Queued, null),
        TorrentState.Metadata => dhtEnabled
            ? (DownloadItemStatus.Queued, "Downloading metadata")
            : (DownloadItemStatus.Warning, "Downloading metadata without DHT, it may never finish"),
        // Warning, not Failed, so failed download handling does not blacklist a release over a local disk error.
        TorrentState.Error => (DownloadItemStatus.Warning, $"Torrent error: {error}"),
        _ => (DownloadItemStatus.Downloading, $"Unknown torrent state: {state}"),
    };

    public static TimeSpan? RemainingTime(DownloadItemStatus status, long remainingSize, long downloadRate) => status switch
    {
        DownloadItemStatus.Completed => TimeSpan.Zero,
        DownloadItemStatus.Downloading when downloadRate > 0 => TimeSpan.FromSeconds(remainingSize / downloadRate),
        _ => null,
    };

    public static bool HasReachedSeedLimit(double ratio, TimeSpan? seedingTime, TorrentEngineOptions options) =>
        (options.SeedRatioLimit is double ratioLimit && ratioLimit - ratio <= RatioTolerance)
        || (options.SeedTimeLimit is TimeSpan timeLimit && seedingTime >= timeLimit);

    // Removal also requires the torrent to be stopped, like upstream requires pausedUP/stoppedUP.
    public static bool CanBeRemoved(TorrentState state, bool complete, double ratio, TimeSpan? seedingTime, TorrentEngineOptions options) =>
        complete && state == TorrentState.Stopped && HasReachedSeedLimit(ratio, seedingTime, options);
}
