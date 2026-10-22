using Modularr.Core.Qualities;

namespace Modularr.Core.Parsing.Movies;

public sealed record ParsedMovieInfo
{
    // First entry is the full parsed title, the rest are its AKA alternatives.
    public required IReadOnlyList<string> MovieTitles { get; init; }
    public required string OriginalTitle { get; init; }
    public required string ReleaseTitle { get; init; }
    public required string SimpleReleaseTitle { get; init; }
    public required QualityModel Quality { get; init; }
    public string? ReleaseGroup { get; init; }
    public string? ReleaseHash { get; init; }
    public string? Edition { get; init; }
    public int? Year { get; init; }
    public string? ImdbId { get; init; }
    public int? TmdbId { get; init; }
    public string? HardcodedSubs { get; init; }

    public string PrimaryMovieTitle => MovieTitles[0];
}
