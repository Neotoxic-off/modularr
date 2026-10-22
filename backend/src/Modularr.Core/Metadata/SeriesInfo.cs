using Modularr.Core.Languages;

namespace Modularr.Core.Metadata;

public enum SeriesStatus
{
    Continuing = 0,
    Ended = 1,
    Upcoming = 2,
}

// A SkyHook show. Episodes are only filled by GetSeriesAsync; search results carry none.
public sealed record SeriesInfo
{
    public required int TvdbId { get; init; }
    public int? TvRageId { get; init; }
    public int? TvMazeId { get; init; }
    public int? TmdbId { get; init; }
    public string? ImdbId { get; init; }
    // Anime cross-references; Sonarr has no anime flag, the series type is the user's choice.
    public IReadOnlyList<int> MalIds { get; init; } = [];
    public IReadOnlyList<int> AniListIds { get; init; } = [];
    public required string Title { get; init; }
    public string? OriginalTitle { get; init; }
    public required Language OriginalLanguage { get; init; }
    public string? OriginalCountry { get; init; }
    public string? Overview { get; init; }
    public DateOnly? FirstAired { get; init; }
    public DateOnly? LastAired { get; init; }
    public int? Year => FirstAired?.Year;
    public int Runtime { get; init; }
    public string? Network { get; init; }
    public TimeOnly? AirTime { get; init; }
    public string? TitleSlug { get; init; }
    public SeriesStatus Status { get; init; }
    public MetadataRating Ratings { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public string? Certification { get; init; }
    // From Sonarr's daily-series list: such shows are numbered by air date.
    public bool IsDaily { get; init; }
    public IReadOnlyList<SeriesActor> Actors { get; init; } = [];
    public IReadOnlyList<SeasonInfo> Seasons { get; init; } = [];
    public IReadOnlyList<MetadataImage> Images { get; init; } = [];
    // Episode orderings available for the show (official, dvd, absolute, ...); pass one to GetSeriesAsync.
    public IReadOnlyList<SeasonTypeInfo> SeasonTypes { get; init; } = [];
    public IReadOnlyList<SeriesTranslation> Translations { get; init; } = [];
    public IReadOnlyList<EpisodeInfo> Episodes { get; init; } = [];
}

public sealed record EpisodeInfo
{
    public required int TvdbId { get; init; }
    public required int SeasonNumber { get; init; }
    public required int EpisodeNumber { get; init; }
    public int? AbsoluteEpisodeNumber { get; init; }
    public int? AiredAfterSeasonNumber { get; init; }
    public int? AiredBeforeSeasonNumber { get; init; }
    public int? AiredBeforeEpisodeNumber { get; init; }
    public string? Title { get; init; }
    public string? Overview { get; init; }
    public DateOnly? AirDate { get; init; }
    public DateTime? AirDateUtc { get; init; }
    public int Runtime { get; init; }
    public string? FinaleType { get; init; }
    public MetadataRating Ratings { get; init; }
    public IReadOnlyList<MetadataImage> Images { get; init; } = [];
}

public sealed record SeasonInfo(int SeasonNumber, string? Title, IReadOnlyList<MetadataImage> Images);

public sealed record SeasonTypeInfo(string Name, string Type, IReadOnlyList<int> SeasonNumbers, int EpisodeCount);

public sealed record SeriesActor(string Name, string? Character, IReadOnlyList<MetadataImage> Images);

public sealed record SeriesTranslation(Language Language, string Title, string? Overview);
