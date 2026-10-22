using Modularr.Core.Decisions;

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
    public bool Monitored { get; set; }
    public required string Path { get; set; }
    public int QualityProfileId { get; set; }
    public QualityProfile QualityProfile { get; set; } = null!;
    public DateTime Added { get; set; }
    public string? Overview { get; set; }

    // URL of the metadata poster at add time.
    public string? Poster { get; set; }
    public ICollection<Season> Seasons { get; } = [];
    public ICollection<Episode> Episodes { get; } = [];
}

// Stored as JSON inside the series row, like Sonarr.
public sealed class Season
{
    public int Number { get; set; }
    public bool Monitored { get; set; }
}
