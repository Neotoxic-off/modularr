using Modularr.Core.Languages;
using Modularr.Core.Qualities;

namespace Modularr.Core.Persistence;

public sealed class BlocklistItem
{
    public int Id { get; set; }
    public int? SeriesId { get; set; }
    public IReadOnlyList<int> EpisodeIds { get; set; } = [];
    public int? MovieId { get; set; }

    // Compared case-insensitively (NOCASE), like upstream BlocklistedByTitle.
    public required string SourceTitle { get; set; }
    public required QualityModel Quality { get; set; }
    public IReadOnlyList<Language> Languages { get; set; } = [];

    // Unique when present, compared case-insensitively.
    public string? InfoHash { get; set; }
    public string? Indexer { get; set; }
    public string? Reason { get; set; }
    public DateTime Date { get; set; }
}
