namespace Modularr.Core.Metadata;

// Same values as upstream MediaCoverTypes.
public enum MediaCoverType
{
    Unknown = 0,
    Poster = 1,
    Banner = 2,
    Fanart = 3,
    Screenshot = 4,
    Headshot = 5,
    Clearlogo = 6,
}

public sealed record MetadataImage(MediaCoverType CoverType, string Url)
{
    // Union of the Sonarr and Radarr cover-type maps (Sonarr ignored headshot, Radarr ignored banner).
    internal static MetadataImage From(string? coverType, string url) => new(coverType?.ToLowerInvariant() switch
    {
        "poster" => MediaCoverType.Poster,
        "banner" => MediaCoverType.Banner,
        "fanart" => MediaCoverType.Fanart,
        "clearlogo" => MediaCoverType.Clearlogo,
        "headshot" => MediaCoverType.Headshot,
        _ => MediaCoverType.Unknown,
    }, url);
}

public static class MetadataImages
{
    public static string? UrlOf(this IReadOnlyList<MetadataImage> images, MediaCoverType type) => images.FirstOrDefault(i => i.CoverType == type)?.Url;
}

public readonly record struct MetadataRating(int Votes, decimal Value);
