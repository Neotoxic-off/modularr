using Modularr.Core.Decisions;

namespace Modularr.Core.Persistence;

public sealed class Movie
{
    public int Id { get; set; }
    public int TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public required string Title { get; set; }
    public int Year { get; set; }

    // Minutes, 0 when unknown; drives the release size limits.
    public int Runtime { get; set; }
    public bool Monitored { get; set; }
    public required string Path { get; set; }
    public int QualityProfileId { get; set; }
    public QualityProfile QualityProfile { get; set; } = null!;
}
