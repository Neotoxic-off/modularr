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

    // Stall check: a download hitting one of these limits is re-announced StallRetries times, then failed, blocklisted and
    // replaced by the next best release.
    [Range(typeof(TimeSpan), "00:01:00", "30.00:00:00")]
    public TimeSpan MetadataTimeout { get; init; } = TimeSpan.FromMinutes(15);

    [Range(typeof(TimeSpan), "00:01:00", "30.00:00:00")]
    public TimeSpan NoPeersTimeout { get; init; } = TimeSpan.FromHours(1);

    [Range(typeof(TimeSpan), "00:01:00", "30.00:00:00")]
    public TimeSpan NoProgressTimeout { get; init; } = TimeSpan.FromHours(2);

    // Bytes per second averaged over SlowSpeedWindow, 0 disables the check.
    [Range(0, int.MaxValue)]
    public int MinimumAverageSpeed { get; init; } = 5 * 1024;

    [Range(typeof(TimeSpan), "00:01:00", "30.00:00:00")]
    public TimeSpan SlowSpeedWindow { get; init; } = TimeSpan.FromHours(6);

    [Range(0, 100)]
    public int StallRetries { get; init; } = 2;

    // Automatic re-grabs in a row for one series or movie after failed downloads, so a bad search result cannot loop.
    [Range(0, 100)]
    public int MaxAutomaticRegrabs { get; init; } = 3;
}
