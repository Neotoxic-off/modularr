using System.Collections.Frozen;
using Modularr.Core.Qualities;

namespace Modularr.Core.Decisions;

// Size limits in megabytes per minute of runtime; null means unlimited (or no preference).
public sealed record QualityDefinition(Quality Quality, double? MinSize, double? MaxSize, double? PreferredSize)
{
    // Sonarr defaults, per episode minute. Qualities Sonarr does not know (CAM, TELESYNC, ...) have no limits.
    public static readonly FrozenDictionary<Quality, QualityDefinition> SeriesDefaults = new QualityDefinition[]
    {
        new(Quality.Unknown, 1, 199.9, 95),
        new(Quality.Sdtv, 2, 100, 95),
        new(Quality.WebRip480p, 2, 100, 95),
        new(Quality.WebDl480p, 2, 100, 95),
        new(Quality.Dvd, 2, 100, 95),
        new(Quality.Bluray480p, 2, 100, 95),
        new(Quality.Bluray576p, 2, 100, 95),
        new(Quality.Hdtv720p, 3, 125, 95),
        new(Quality.Hdtv1080p, 4, 125, 95),
        new(Quality.RawHd, 4, null, 95),
        new(Quality.WebRip720p, 3, 130, 95),
        new(Quality.WebDl720p, 3, 130, 95),
        new(Quality.Bluray720p, 4, 130, 95),
        new(Quality.WebRip1080p, 4, 130, 95),
        new(Quality.WebDl1080p, 4, 130, 95),
        new(Quality.Bluray1080p, 4, 155, 95),
        new(Quality.Remux1080p, 35, null, 95),
        new(Quality.Hdtv2160p, 35, 199.9, 95),
        new(Quality.WebRip2160p, 35, null, 95),
        new(Quality.WebDl2160p, 35, null, 95),
        new(Quality.Bluray2160p, 35, null, 95),
        new(Quality.Remux2160p, 35, null, 95),
    }.ToFrozenDictionary(d => d.Quality);

    // Radarr defaults, per movie minute.
    public static readonly FrozenDictionary<Quality, QualityDefinition> MovieDefaults = new QualityDefinition[]
    {
        new(Quality.Unknown, 0, 100, 95),
        new(Quality.Workprint, 0, 100, 95),
        new(Quality.Cam, 0, 100, 95),
        new(Quality.Telesync, 0, 100, 95),
        new(Quality.Telecine, 0, 100, 95),
        new(Quality.Regional, 0, 100, 95),
        new(Quality.DvdScr, 0, 100, 95),
        new(Quality.Sdtv, 0, 100, 95),
        new(Quality.Dvd, 0, 100, 95),
        new(Quality.DvdR, 0, 100, 95),
        new(Quality.WebDl480p, 0, 100, 95),
        new(Quality.WebRip480p, 0, 100, 95),
        new(Quality.Bluray480p, 0, 100, 95),
        new(Quality.Bluray576p, 0, 100, 95),
        new(Quality.Hdtv720p, 0, 100, 95),
        new(Quality.WebDl720p, 0, 100, 95),
        new(Quality.WebRip720p, 0, 100, 95),
        new(Quality.Bluray720p, 0, 100, 95),
        new(Quality.Hdtv1080p, 0, 100, 95),
        new(Quality.WebDl1080p, 0, 100, 95),
        new(Quality.WebRip1080p, 0, 100, 95),
        new(Quality.Bluray1080p, 0, null, null),
        new(Quality.Remux1080p, 0, null, null),
        new(Quality.Hdtv2160p, 0, null, null),
        new(Quality.WebDl2160p, 0, null, null),
        new(Quality.WebRip2160p, 0, null, null),
        new(Quality.Bluray2160p, 0, null, null),
        new(Quality.Remux2160p, 0, null, null),
        new(Quality.BrDisk, 0, null, null),
        new(Quality.RawHd, 0, null, null),
    }.ToFrozenDictionary(d => d.Quality);

    public static FrozenDictionary<Quality, QualityDefinition> DefaultsFor(MediaType mediaType) =>
        mediaType == MediaType.Movie ? MovieDefaults : SeriesDefaults;
}
