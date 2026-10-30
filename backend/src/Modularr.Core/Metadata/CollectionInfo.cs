namespace Modularr.Core.Metadata;

// A TMDB collection (Id > 0) or a keyword franchise (Id < 0, see CollectionFollow), parts in release order.
public sealed record CollectionInfo
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string? Overview { get; init; }
    public string? Poster { get; init; }
    public string? Backdrop { get; init; }
    public IReadOnlyList<CollectionPart> Parts { get; init; } = [];

    // Undated parts last.
    public static IReadOnlyList<CollectionPart> InReleaseOrder(IEnumerable<CollectionPart> parts) =>
        [.. parts.DistinctBy(p => p.TmdbId).OrderBy(p => p.Release ?? DateOnly.MaxValue).ThenBy(p => p.Year).ThenBy(p => p.Title, StringComparer.Ordinal)];
}

// Release is the first day it was out anywhere (cinema, digital or physical).
public sealed record CollectionPart(int TmdbId, string Title, int Year, DateOnly? Release, string? Poster, string? Backdrop)
{
    public bool IsReleased(DateOnly today) => Release <= today;
}

// A collection search hit.
public sealed record CollectionSummary(int Id, string Name, string? Poster, string? Backdrop);
