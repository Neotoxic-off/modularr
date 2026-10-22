namespace Modularr.Core.Downloads.Torrents;

// Mirrors the fields of Sonarr/Radarr DownloadClientItem the import pipeline reads.
public sealed record TorrentStatus
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Category { get; init; }

    public required DownloadItemStatus Status { get; init; }

    public string? Message { get; init; }

    // 0 to 1.
    public required double Progress { get; init; }

    public required long TotalSize { get; init; }

    public required long RemainingSize { get; init; }

    public TimeSpan? RemainingTime { get; init; }

    public required long DownloadRate { get; init; }

    public required long UploadRate { get; init; }

    public required double SeedRatio { get; init; }

    public required string SavePath { get; init; }

    // The single file for a one-file torrent, otherwise the torrent folder.
    public required string OutputPath { get; init; }

    public required IReadOnlyList<TorrentFileStatus> Files { get; init; }

    // Stopped after reaching the seed limit, so the files can be moved and the torrent removed.
    public required bool CanBeRemoved { get; init; }

    // What the stall check watches: a magnet without metadata, connected seeds and leechers, payload bytes received this session.
    public bool HasMetadata { get; init; }

    public int Seeds { get; init; }

    public int Peers { get; init; }

    public long DownloadedBytes { get; init; }
}

public sealed record TorrentFileStatus(string Path, long Size, double Progress);
