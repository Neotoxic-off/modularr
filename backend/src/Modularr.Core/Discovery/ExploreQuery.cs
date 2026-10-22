using Modularr.Core.Decisions;
using Modularr.Core.Languages;
using Modularr.Core.Metadata;

namespace Modularr.Core.Discovery;

public enum ExploreSort
{
    Popularity,
    Rating,
    Date,
    Title,
}

// Movies are Released or Upcoming; series and anime Continuing, Ended or Upcoming.
public enum ExploreStatus
{
    Released,
    Upcoming,
    Continuing,
    Ended,
}

// The filters of /api/explore, each optional. Page is 1-based; Year narrows the yearFrom-yearTo window to one year.
public sealed record ExploreQuery
{
    public const int MaxPage = 500;
    public const int MinYear = 1870;
    public const int MaxYear = 2100;

    public int Page { get; init; } = 1;
    public string? Genre { get; init; }
    public int? Year { get; init; }
    public int? YearFrom { get; init; }
    public int? YearTo { get; init; }

    // 0-10.
    public decimal? MinRating { get; init; }

    // The original language.
    public Language? Language { get; init; }
    public ExploreStatus? Status { get; init; }
    public ExploreSort Sort { get; init; } = ExploreSort.Popularity;

    public int? FirstYear => Year ?? YearFrom;
    public int? LastYear => Year ?? YearTo;

    public ExploreQuery EnsureValid(MediaType kind)
    {
        string? error = this switch
        {
            { Page: < 1 or > MaxPage } => $"page must be between 1 and {MaxPage}.",
            { Genre.Length: > 64 } => "genre is too long.",
            _ when new[] { Year, YearFrom, YearTo }.Any(y => y is < MinYear or > MaxYear) => $"Years must be between {MinYear} and {MaxYear}.",
            { YearFrom: int from, YearTo: int to } when from > to => "yearFrom must not be after yearTo.",
            { MinRating: < 0 or > 10 } => "minRating must be between 0 and 10.",
            { Language: Language language } when language.Id <= 0 => $"{language} is not a language to explore.",
            { Status: ExploreStatus.Released } when kind != MediaType.Movie => "A show status is continuing, ended or upcoming.",
            { Status: ExploreStatus.Continuing or ExploreStatus.Ended } when kind == MediaType.Movie => "A movie status is released or upcoming.",
            _ => null,
        };
        return error is null ? this : throw new ArgumentException(error);
    }

    // For the lists filtered here (the TVmaze index, the keyless movie lists).
    public bool Matches(DiscoveryItem item, DateOnly? date)
    {
        ArgumentNullException.ThrowIfNull(item);
        int? year = date?.Year ?? item.Year;
        return (Genre is null || item.Genres.Contains(Genre, StringComparer.OrdinalIgnoreCase))
            && (FirstYear is null || year >= FirstYear)
            && (LastYear is null || year <= LastYear)
            && (MinRating is null || item.Rating >= MinRating)
            && (Language is null || string.Equals(item.Language, Language.Name, StringComparison.OrdinalIgnoreCase))
            && (Status is null || HasStatus(item.Status));
    }

    // Movie statuses are RadarrAPI's: in cinemas counts as released, announced and TBA as upcoming.
    private bool HasStatus(string? status) => Status switch
    {
        ExploreStatus.Released => status is nameof(MovieStatus.Released) or nameof(MovieStatus.InCinemas),
        ExploreStatus.Upcoming => status is nameof(ExploreStatus.Upcoming) or nameof(MovieStatus.Announced) or nameof(MovieStatus.Tba),
        _ => status == Status.ToString(),
    };
}

// One page of /api/explore. Limited: movies without a TMDB key, from the keyless lists only. Loading: the series index is still
// being fetched, so more titles will come.
public sealed record ExplorePage(IReadOnlyList<DiscoveryItem> Items, int Page, bool HasMore, bool Limited = false, bool Loading = false, string? Error = null);
