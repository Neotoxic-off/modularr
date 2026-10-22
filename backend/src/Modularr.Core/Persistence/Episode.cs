namespace Modularr.Core.Persistence;

public sealed class Episode
{
    public int Id { get; set; }
    public int SeriesId { get; set; }
    public Series Series { get; set; } = null!;
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public int? AbsoluteEpisodeNumber { get; set; }
    public DateOnly? AirDate { get; set; }
    public string? Title { get; set; }
    public bool Monitored { get; set; }
    public int? MediaFileId { get; set; }
}
