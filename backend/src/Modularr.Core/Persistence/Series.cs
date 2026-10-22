using Modularr.Core.Decisions;
using Modularr.Core.Languages;

namespace Modularr.Core.Persistence;

// Same values as Sonarr SeriesTypes.
public enum SeriesType
{
    Standard = 0,
    Daily = 1,
    Anime = 2,
}

public sealed class Series
{
    public int Id { get; set; }
    public int TvdbId { get; set; }
    public required string Title { get; set; }
    public required string SortTitle { get; set; }
    public int Year { get; set; }

    // Minutes per episode, 0 when unknown; drives the release size limits.
    public int Runtime { get; set; }
    public SeriesType SeriesType { get; set; }

    // Get-only, not mapped: anime is the series type upstream, its own category for profiles and languages here.
    public MediaType MediaType => SeriesType == SeriesType.Anime ? MediaType.Anime : MediaType.Series;
    public bool Monitored { get; set; }
    public required string Path { get; set; }
    public int QualityProfileId { get; set; }
    public QualityProfile QualityProfile { get; set; } = null!;
    public DateTime Added { get; set; }

    // Unknown for series added before it was stored.
    public Language OriginalLanguage { get; set; } = Language.Unknown;

    // Wanted version and subtitle languages for this series only; null follows MediaOptions.
    public ReleaseVersion? Version { get; set; }
    public IReadOnlyList<Language>? Subtitles { get; set; }
    public string? Overview { get; set; }

    // Metadata at add time: poster and fanart URLs, a 0-10 rating, genres and the (US) certification.
    public string? Poster { get; set; }
    public string? Backdrop { get; set; }
    public decimal? Rating { get; set; }
    public IReadOnlyList<string> Genres { get; set; } = [];
    public string? Certification { get; set; }
    public ICollection<Season> Seasons { get; } = [];
    public ICollection<Episode> Episodes { get; } = [];
}

// Stored as JSON inside the series row, like Sonarr.
public sealed class Season
{
    public int Number { get; set; }
    public bool Monitored { get; set; }
}
