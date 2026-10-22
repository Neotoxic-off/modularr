using Modularr.Core.Decisions;
using Modularr.Core.Qualities;

namespace Modularr.Core.Persistence;

// The six Sonarr/Radarr default profiles, merged; seeded by the initial migration.
// Conflicts, Radarr picked each time:
// - order: Radarr's weights (resolution first, Remux after Bluray, BR-DISK then Raw-HD last); Sonarr ranks HDTV-1080p and Raw-HD
//   below WEB 720p and WEB 480p below DVD.
// - allowed: Radarr's lists, a superset of Sonarr's (adds the pre-release sources, DVD-R, 2160p in Any, the Remuxes).
// - cutoffs: Sonarr cuts at the first TV source (SDTV, HDTV-720p, HDTV-1080p, HDTV-2160p), Radarr at Bluray/Remux.
public static class DefaultQualityProfiles
{
    private static readonly IReadOnlyList<Quality[]> Ranking =
    [
        [Quality.Unknown], [Quality.Workprint], [Quality.Cam], [Quality.Telesync], [Quality.Telecine], [Quality.Regional],
        [Quality.DvdScr], [Quality.Sdtv], [Quality.Dvd], [Quality.DvdR],
        [Quality.WebDl480p, Quality.WebRip480p], [Quality.Bluray480p], [Quality.Bluray576p],
        [Quality.Hdtv720p], [Quality.WebDl720p, Quality.WebRip720p], [Quality.Bluray720p],
        [Quality.Hdtv1080p], [Quality.WebDl1080p, Quality.WebRip1080p], [Quality.Bluray1080p], [Quality.Remux1080p],
        [Quality.Hdtv2160p], [Quality.WebDl2160p, Quality.WebRip2160p], [Quality.Bluray2160p], [Quality.Remux2160p],
        [Quality.BrDisk], [Quality.RawHd],
    ];

    // Fresh instances on each call: entities are tracked.
    public static QualityProfile[] Create() =>
    [
        Profile(1, "Any", Quality.Bluray480p,
            Quality.Workprint, Quality.Cam, Quality.Telesync, Quality.Telecine, Quality.DvdScr, Quality.Regional, Quality.Sdtv,
            Quality.Dvd, Quality.DvdR, Quality.Hdtv720p, Quality.Hdtv1080p, Quality.Hdtv2160p, Quality.WebDl480p, Quality.WebRip480p,
            Quality.WebDl720p, Quality.WebRip720p, Quality.WebDl1080p, Quality.WebRip1080p, Quality.WebDl2160p, Quality.WebRip2160p,
            Quality.Bluray480p, Quality.Bluray576p, Quality.Bluray720p, Quality.Bluray1080p, Quality.Bluray2160p,
            Quality.Remux1080p, Quality.Remux2160p, Quality.BrDisk),
        Profile(2, "SD", Quality.Bluray480p,
            Quality.Workprint, Quality.Cam, Quality.Telesync, Quality.Telecine, Quality.DvdScr, Quality.Regional, Quality.Sdtv,
            Quality.Dvd, Quality.WebDl480p, Quality.WebRip480p, Quality.Bluray480p, Quality.Bluray576p),
        Profile(3, "HD-720p", Quality.Bluray720p,
            Quality.Hdtv720p, Quality.WebDl720p, Quality.WebRip720p, Quality.Bluray720p),
        Profile(4, "HD-1080p", Quality.Bluray1080p,
            Quality.Hdtv1080p, Quality.WebDl1080p, Quality.WebRip1080p, Quality.Bluray1080p, Quality.Remux1080p),
        Profile(5, "Ultra-HD", Quality.Remux2160p,
            Quality.Hdtv2160p, Quality.WebDl2160p, Quality.WebRip2160p, Quality.Bluray2160p, Quality.Remux2160p),
        Profile(6, "HD - 720p/1080p", Quality.Bluray720p,
            Quality.Hdtv720p, Quality.Hdtv1080p, Quality.WebDl720p, Quality.WebRip720p, Quality.WebDl1080p, Quality.WebRip1080p,
            Quality.Bluray720p, Quality.Bluray1080p, Quality.Remux1080p),
    ];

    // Upstream GetDefaultProfile: a group is allowed when any of its qualities is, and so are all its members.
    private static QualityProfile Profile(int id, string name, Quality cutoff, params Quality[] allowed) => new()
    {
        Id = id,
        Name = name,
        Cutoff = cutoff.Id,
        Items = [.. Ranking.Select((group, index) => Item(group, group.Any(allowed.Contains), index))],
    };

    private static QualityProfileQualityItem Item(Quality[] group, bool allowed, int index) => group.Length == 1
        ? new QualityProfileQualityItem { Quality = group[0], Allowed = allowed }
        : new QualityProfileQualityItem
        {
            Id = QualityProfileQualityItem.FirstGroupId + index,
            Name = $"WEB {group[0].Resolution}p",
            Items = [.. group.Select(q => new QualityProfileQualityItem { Quality = q, Allowed = allowed })],
            Allowed = allowed,
        };
}
