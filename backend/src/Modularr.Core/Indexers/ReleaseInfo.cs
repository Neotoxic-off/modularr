namespace Modularr.Core.Indexers;

// One search result from an indexer (Prowlarr ReleaseInfo + TorrentInfo, torrent-only, video-only fields).
public sealed record ReleaseInfo
{
    public required string Guid { get; init; }
    public required string Title { get; init; }
    public required string Indexer { get; init; }
    public DateTimeOffset PublishDate { get; init; }
    public long? Size { get; init; }
    public string? DownloadUrl { get; init; }
    public string? MagnetUrl { get; init; }
    public string? InfoHash { get; init; }
    public string? InfoUrl { get; init; }
    public int? Seeders { get; init; }
    public int? Peers { get; init; }
    public int? Grabs { get; init; }
    public double? DownloadVolumeFactor { get; init; }
    public double? UploadVolumeFactor { get; init; }
    public IReadOnlyList<int> Categories { get; init; } = [];
    public int? ImdbId { get; init; }
    public int? TmdbId { get; init; }
    public int? TvdbId { get; init; }
}
