using System.ComponentModel.DataAnnotations;

namespace Modularr.Core.Downloads.Torrents;

public sealed record TorrentEngineOptions
{
    // Holds engine state, fast-resume data, metadata cache and torrent tags; relative to the config directory.
    [Required]
    public string StateDirectory { get; init; } = "torrents";

    [Range(1, 65535)]
    public int ListenPort { get; init; } = 6881;

    [Range(1, int.MaxValue)]
    public int MaxConnections { get; init; } = 150;

    // Bytes per second, 0 means unlimited.
    [Range(0, int.MaxValue)]
    public int MaxDownloadRate { get; init; }

    [Range(0, int.MaxValue)]
    public int MaxUploadRate { get; init; }

    public bool DhtEnabled { get; init; } = true;

    public bool LocalPeerDiscoveryEnabled { get; init; } = true;

    // Once either limit is reached a completed torrent is stopped and becomes removable.
    [Range(0, double.MaxValue)]
    public double? SeedRatioLimit { get; init; }

    [Range(typeof(TimeSpan), "00:00:00", "3650.00:00:00")]
    public TimeSpan? SeedTimeLimit { get; init; }
}
