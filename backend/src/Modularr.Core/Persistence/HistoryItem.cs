using Modularr.Core.Languages;
using Modularr.Core.Qualities;

namespace Modularr.Core.Persistence;

// Values follow Sonarr EpisodeHistoryEventType (Grabbed, DownloadFolderImported, DownloadFailed, EpisodeFileDeleted).
public enum HistoryEventType
{
    Grabbed = 1,
    Imported = 3,
    Failed = 4,
    Deleted = 5,
}

public sealed class HistoryItem
{
    public int Id { get; set; }
    public HistoryEventType EventType { get; set; }
    public int? SeriesId { get; set; }
    public int? EpisodeId { get; set; }
    public int? MovieId { get; set; }
    public required string SourceTitle { get; set; }
    public required QualityModel Quality { get; set; }
    public IReadOnlyList<Language> Languages { get; set; } = [];
    public string? InfoHash { get; set; }
    public Dictionary<string, string> Data { get; } = [];
    public DateTime Date { get; set; }
}
