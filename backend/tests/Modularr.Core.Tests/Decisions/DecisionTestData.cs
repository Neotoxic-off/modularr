using Modularr.Core.Decisions;
using Modularr.Core.Indexers;
using Modularr.Core.Qualities;

namespace Modularr.Core.Tests.Decisions;

internal static class DecisionTestData
{
    public const long Megabyte = 1024 * 1024;

    // Upstream QualityFixture.GetDefaultQualities lists (Sonarr for series, Radarr for movies).
    private static readonly Quality[] SonarrQualities =
    [
        Quality.Unknown, Quality.Sdtv, Quality.WebDl480p, Quality.Dvd, Quality.Hdtv720p, Quality.Hdtv1080p, Quality.Hdtv2160p,
        Quality.RawHd, Quality.WebDl720p, Quality.WebDl1080p, Quality.WebDl2160p, Quality.Bluray720p, Quality.Bluray1080p, Quality.Bluray2160p,
    ];

    private static readonly Quality[] RadarrQualities =
    [
        Quality.Unknown, Quality.Cam, Quality.Telecine, Quality.DvdScr, Quality.Regional, Quality.Sdtv, Quality.Dvd, Quality.DvdR,
        Quality.Hdtv720p, Quality.Hdtv1080p, Quality.Hdtv2160p, Quality.WebDl480p, Quality.WebDl720p, Quality.WebDl1080p, Quality.WebDl2160p,
        Quality.Bluray480p, Quality.Bluray576p, Quality.Bluray720p, Quality.Bluray1080p, Quality.Bluray2160p, Quality.BrDisk, Quality.RawHd,
    ];

    // Not allowed qualities first, then the allowed ones in the given order; all allowed when none given.
    public static IReadOnlyList<QualityProfileQualityItem> DefaultQualities(MediaType mediaType, params Quality[] allowed)
    {
        Quality[] qualities = mediaType == MediaType.Movie ? RadarrQualities : SonarrQualities;

        if (allowed.Length == 0)
        {
            allowed = qualities;
        }

        return [.. qualities.Except(allowed).Concat(allowed).Select(q => new QualityProfileQualityItem { Quality = q, Allowed = allowed.Contains(q) })];
    }

    public static QualityProfile Profile(MediaType mediaType, int cutoff = 0, bool upgradeAllowed = false) =>
        new() { Items = DefaultQualities(mediaType), Cutoff = cutoff, UpgradeAllowed = upgradeAllowed };

    public static ReleaseInfo Release(string title = "Title.S01E01.720p.HDTV-Group", long? size = null) =>
        new() { Id = "1", Title = title, Indexer = "Indexer", Size = size };

    public static ReleaseCandidate Candidate(MediaType mediaType, QualityModel? quality = null, QualityProfile? profile = null) =>
        new()
        {
            Release = Release(),
            MediaType = mediaType,
            Profile = profile ?? Profile(mediaType),
            Quality = quality ?? new QualityModel(Quality.Sdtv, new Revision(2)),
        };

    public static EpisodeTarget Episode(int episodeNumber = 1, int runtime = 0, ExistingFile? file = null) =>
        new() { SeasonNumber = 1, EpisodeNumber = episodeNumber, Runtime = runtime, File = file };

    public static DecisionContext Context(DecisionOptions? options = null) => new() { Options = options ?? new DecisionOptions() };

    public static QualityModel Q(Quality quality, int version = 1, int real = 0) => new(quality, new Revision(version, real));
}
