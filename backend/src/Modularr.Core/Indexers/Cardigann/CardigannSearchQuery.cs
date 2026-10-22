using System.Globalization;

namespace Modularr.Core.Indexers.Cardigann;

public enum CardigannSearchType
{
    Search,
    TvSearch,
    MovieSearch,
}

// Prowlarr Basic/Tv/MovieSearchCriteria, reduced to the TV and movie fields Sonarr/Radarr send.
public sealed record CardigannSearchQuery
{
    public CardigannSearchType Type { get; init; } = CardigannSearchType.Search;

    // Free text; empty means "latest releases" (RSS) unless an id is given.
    public string? Term { get; init; }

    // Torznab category ids (e.g. 5000 TV, 2040 Movies/HD); empty uses the definition's default categories.
    public IReadOnlyList<int> Categories { get; init; } = [];

    public int? Season { get; init; }

    // A number, or "MM/dd" with Season as the year for daily shows.
    public string? Episode { get; init; }

    public int? Year { get; init; }
    public int? ImdbId { get; init; }
    public int? TmdbId { get; init; }
    public int? TvdbId { get; init; }

    internal string TypeName => Type switch
    {
        CardigannSearchType.TvSearch => "tvsearch",
        CardigannSearchType.MovieSearch => "movie",
        _ => "search",
    };

    internal string? FullImdbId => ParseUtil.GetFullImdbId(ImdbId?.ToString(CultureInfo.InvariantCulture));

    internal bool IsIdSearch => Type switch
    {
        CardigannSearchType.TvSearch => ImdbId is not null || TvdbId is not null || TmdbId is not null,
        CardigannSearchType.MovieSearch => ImdbId is not null || TmdbId is not null || Year is not null,
        _ => false,
    };

    internal bool IsRssSearch => string.IsNullOrWhiteSpace(Term) && !IsIdSearch;

    // "S01E02", "S01", or "2024.01.31" for a daily episode; empty without a season.
    internal string EpisodeSearchString
    {
        get
        {
            if (Type != CardigannSearchType.TvSearch || Season is null or 0)
            {
                return "";
            }

            if (DateTime.TryParseExact($"{Season} {Episode}", "yyyy MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime airDate))
            {
                return airDate.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
            }

            if (string.IsNullOrWhiteSpace(Episode))
            {
                return $"S{Season:00}";
            }

            try
            {
                return $"S{Season:00}E{ParseUtil.CoerceInt(Episode):00}";
            }
            catch (FormatException)
            {
                return $"S{Season:00}E{Episode}";
            }
        }
    }
}
