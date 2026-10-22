using Modularr.Core.Decisions;
using Modularr.Core.Languages;

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

    // Unknown for movies added before it was stored.
    public Language OriginalLanguage { get; set; } = Language.Unknown;

    // Wanted version and subtitle languages for this movie only; null follows MediaOptions.
    public ReleaseVersion? Version { get; set; }
    public IReadOnlyList<Language>? Subtitles { get; set; }
    public string? Overview { get; set; }

    // Metadata at add time: poster and fanart URLs, a 0-10 rating, genres and the (US) certification.
    public string? Poster { get; set; }
    public string? Backdrop { get; set; }
    public decimal? Rating { get; set; }
    public IReadOnlyList<string> Genres { get; set; } = [];
    public string? Certification { get; set; }
}
