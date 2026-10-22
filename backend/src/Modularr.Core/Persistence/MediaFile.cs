using Modularr.Core.Languages;
using Modularr.Core.Qualities;

namespace Modularr.Core.Persistence;

// An imported file: Sonarr EpisodeFile (episodes point at it through Episode.MediaFileId) or Radarr MovieFile.
public sealed class MediaFile
{
    public int Id { get; set; }
    public int? SeriesId { get; set; }
    public int? MovieId { get; set; }
    public required string Path { get; set; }
    public long Size { get; set; }
    public required QualityModel Quality { get; set; }
    public IReadOnlyList<Language> Languages { get; set; } = [];
    public string? ReleaseGroup { get; set; }
    public DateTime DateAdded { get; set; }
}
