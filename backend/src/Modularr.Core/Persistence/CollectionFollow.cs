namespace Modularr.Core.Persistence;

// A collection the user follows. TmdbId > 0 is a TMDB collection; < 0 is a franchise defined by a TMDB keyword (-180547 is the MCU).
public sealed class CollectionFollow
{
    public int Id { get; set; }
    public int TmdbId { get; set; }
    public required string Name { get; set; }
    public string? Poster { get; set; }
    public string? Backdrop { get; set; }

    // The metadata refresh adds new parts: released ones searched, upcoming ones monitored for the scheduled search.
    public bool Monitored { get; set; }

    // Profile for the parts it adds; null (or a deleted profile) is the movie default.
    public int? QualityProfileId { get; set; }
    public DateTime Added { get; set; }
}
