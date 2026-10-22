using Modularr.Core.Languages;

namespace Modularr.Core.Metadata;

// Same values as upstream MovieStatusType (minus the persistence-only Deleted).
public enum MovieStatus
{
    Tba = 0,
    Announced = 1,
    InCinemas = 2,
    Released = 3,
}

// A RadarrAPI (TMDB-backed) movie.
public sealed record MovieInfo
{
    public required int TmdbId { get; init; }
    public string? ImdbId { get; init; }
    public required string Title { get; init; }
    public string? OriginalTitle { get; init; }
    public required Language OriginalLanguage { get; init; }
    public string? Overview { get; init; }
    public int Year { get; init; }
    // The premiere year when it differs from TMDB's year.
    public int? SecondaryYear { get; init; }
    public DateTime? InCinemas { get; init; }
    public DateTime? PhysicalRelease { get; init; }
    public DateTime? DigitalRelease { get; init; }
    public MovieStatus Status { get; init; }
    public int Runtime { get; init; }
    public float Popularity { get; init; }
    public string? Certification { get; init; }
    public MovieRatings Ratings { get; init; } = new();
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public IReadOnlyList<MetadataImage> Images { get; init; } = [];
    public IReadOnlyList<string> AlternativeTitles { get; init; } = [];
    public IReadOnlyList<MovieTranslation> Translations { get; init; } = [];
    public IReadOnlyList<int> RecommendationTmdbIds { get; init; } = [];
    public string? Website { get; init; }
    public string? YouTubeTrailerId { get; init; }
    public string? Studio { get; init; }
    public int? CollectionTmdbId { get; init; }
    public string? CollectionTitle { get; init; }
}

public sealed record MovieRatings
{
    public MetadataRating? Tmdb { get; init; }
    public MetadataRating? Imdb { get; init; }
    public MetadataRating? Metacritic { get; init; }
    public MetadataRating? RottenTomatoes { get; init; }
    public MetadataRating? Trakt { get; init; }

    // The one 0-10 score shown to users: IMDb's, else TMDB's (the others are percentages).
    public decimal? Rating => (Imdb ?? Tmdb)?.Value;
}

// Language is null when RadarrAPI sends a code modularr does not know.
public sealed record MovieTranslation(Language? Language, string? Title, string? Overview);
