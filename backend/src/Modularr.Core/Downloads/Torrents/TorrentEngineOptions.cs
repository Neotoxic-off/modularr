namespace Modularr.Core.Downloads.Torrents;

public sealed record TorrentEngineOptions
{
    // Holds engine state, fast-resume data, metadata cache and torrent tags.
    public required string StateDirectory { get; init; }

    public int ListenPort { get; init; } = 6881;

    public int MaxConnections { get; init; } = 150;

    // Bytes per second, 0 means unlimited.
    public int MaxDownloadRate { get; init; }

    public int MaxUploadRate { get; init; }

    public bool DhtEnabled { get; init; } = true;

    public bool LocalPeerDiscoveryEnabled { get; init; } = true;

    // Once either limit is reached a completed torrent is stopped and becomes removable.
    public double? SeedRatioLimit { get; init; }

    public TimeSpan? SeedTimeLimit { get; init; }
}
