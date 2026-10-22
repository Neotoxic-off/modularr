// Ported from Sonarr and Radarr DecisionEngineTests/UpgradeDiskSpecificationFixture: shared cases run for series and movies,
// multi-episode and season pack cases are Sonarr only. Custom format cases keep their quality-only assertions.
using Modularr.Core.Decisions;
using Modularr.Core.Qualities;
using static Modularr.Core.Tests.Decisions.DecisionTestData;

namespace Modularr.Core.Tests.Decisions;

public sealed class UpgradeDiskSpecificationTests
{
    public static TheoryData<MediaType> MediaTypes => [MediaType.Series, MediaType.Movie];

    private static readonly QualityModel Bluray1080pV2 = Q(Quality.Bluray1080p, 2);

    // One existing file (series: plus a missing episode), wanting a DVD v2 by default.
    private static ReleaseCandidate Single(MediaType mediaType, QualityModel? file = null, QualityModel? newQuality = null, QualityProfile? profile = null)
    {
        ExistingFile existing = new(file ?? Bluray1080pV2);

        return Candidate(mediaType, newQuality ?? Q(Quality.Dvd, 2), profile ?? Profile(mediaType, Quality.Bluray1080p.Id, upgradeAllowed: true)) with
        {
            MovieFile = mediaType == MediaType.Movie ? existing : null,
            Episodes = mediaType == MediaType.Movie ? [] : [Episode(1, file: existing), Episode(2)],
        };
    }

    private static ReleaseCandidate Multi(QualityModel? first = null, QualityModel? second = null) =>
        Candidate(MediaType.Series, Q(Quality.Dvd, 2), Profile(MediaType.Series, Quality.Bluray1080p.Id, upgradeAllowed: true)) with
        {
            Episodes = [Episode(1, file: new ExistingFile(first ?? Bluray1080pV2)), Episode(2, file: new ExistingFile(second ?? Bluray1080pV2)), Episode(3)],
        };

    private static bool Accepted(ReleaseCandidate candidate, DecisionOptions? options = null) =>
        DownloadSpecifications.UpgradeDisk(candidate, Context(options)) is null;

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void AcceptsWithoutExistingFile(MediaType mediaType)
    {
        ReleaseCandidate candidate = Single(mediaType) with { MovieFile = null, Episodes = [Episode(1), Episode(2)] };

        Assert.True(Accepted(candidate));
    }

    [Fact]
    public void AcceptsWithoutEpisodes()
    {
        Assert.True(Accepted(Single(MediaType.Series) with { Episodes = [] }));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void AcceptsWhenTheOnlyFileIsUpgradable(MediaType mediaType)
    {
        Assert.True(Accepted(Single(mediaType, Q(Quality.Sdtv))));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void MultiEpisodeNeedsEveryFileUpgradable(bool firstUpgradable, bool secondUpgradable, bool expected)
    {
        Assert.Equal(expected, Accepted(Multi(firstUpgradable ? Q(Quality.Sdtv) : null, secondUpgradable ? Q(Quality.Sdtv) : null)));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void RejectsSameQuality(MediaType mediaType)
    {
        Assert.False(Accepted(Single(mediaType, Q(Quality.WebDl1080p), Q(Quality.WebDl1080p))));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void RejectsRevisionDowngradeWhenPropersArePreferred(MediaType mediaType)
    {
        Rejection? rejection = DownloadSpecifications.UpgradeDisk(Single(mediaType, Q(Quality.WebDl1080p, 2), Q(Quality.WebDl1080p)), Context());

        Assert.Equal(DownloadRejectionReason.DiskHigherRevision, rejection?.Reason);
    }

    [Theory]
    [InlineData(MediaType.Series, 4, 2, 2, 2, false)]
    [InlineData(MediaType.Series, 7, 2, 2, 2, false)]
    [InlineData(MediaType.Series, 4, 1, 4, 2, true)]
    [InlineData(MediaType.Series, 4, 2, 7, 2, false)]
    [InlineData(MediaType.Series, 1, 2, 7, 2, true)]
    [InlineData(MediaType.Series, 1, 2, 2, 2, true)]
    [InlineData(MediaType.Series, 4, 1, 7, 1, false)]
    [InlineData(MediaType.Movie, 4, 2, 2, 2, false)]
    [InlineData(MediaType.Movie, 7, 2, 2, 2, false)]
    [InlineData(MediaType.Movie, 4, 1, 4, 2, true)]
    [InlineData(MediaType.Movie, 4, 2, 7, 2, false)]
    [InlineData(MediaType.Movie, 1, 2, 7, 2, true)]
    [InlineData(MediaType.Movie, 1, 2, 2, 2, true)]
    [InlineData(MediaType.Movie, 4, 1, 7, 1, false)]
    public void HonoursHdtv720pCutoff(MediaType mediaType, int fileQualityId, int fileVersion, int newQualityId, int newVersion, bool expected)
    {
        // Quality ids: 1 SDTV, 2 DVD, 4 HDTV-720p, 7 Bluray-1080p. Cases: equal to cutoff, above cutoff, new proper, cutoff met with
        // better quality (also the "language met/higher" and custom format variants), cutoff not met with better quality, cutoff not met.
        QualityProfile profile = Profile(mediaType, Quality.Hdtv720p.Id, upgradeAllowed: true);

        Assert.Equal(expected, Accepted(Single(mediaType, Q(Quality.FindById(fileQualityId), fileVersion), Q(Quality.FindById(newQualityId), newVersion), profile)));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void AcceptsRevisionUpgradeWhenCutoffIsMet(MediaType mediaType)
    {
        QualityProfile profile = Profile(mediaType, Quality.Hdtv1080p.Id, upgradeAllowed: true);

        Assert.True(Accepted(Single(mediaType, Q(Quality.WebDl1080p, 1), Q(Quality.WebDl1080p, 2), profile)));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void RejectsWhenUpgradesAreDisabledEvenWithHighestCutoff(MediaType mediaType)
    {
        QualityProfile profile = Profile(mediaType, Quality.RawHd.Id, upgradeAllowed: false);

        Assert.False(Accepted(Single(mediaType, Q(Quality.WebDl1080p), Q(Quality.Bluray1080p), profile)));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void RejectsSameQualityWhenUpgradesAreDisabled(MediaType mediaType)
    {
        QualityProfile profile = Profile(mediaType, Quality.Sdtv.Id, upgradeAllowed: false);

        Assert.False(Accepted(Single(mediaType, Q(Quality.WebDl1080p), Q(Quality.WebDl1080p), profile)));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void RejectsRevisionUpgradeWhenUpgradesAreDisabledAndPropersAreNotPreferred(MediaType mediaType)
    {
        QualityProfile profile = Profile(mediaType, Quality.Sdtv.Id, upgradeAllowed: false);
        ReleaseCandidate candidate = Single(mediaType, Q(Quality.WebDl1080p, 1), Q(Quality.WebDl1080p, 2), profile);

        Assert.False(Accepted(candidate, new DecisionOptions { DownloadPropersAndRepacks = ProperDownloadType.DoNotPrefer }));
    }

    private static ReleaseCandidate SeasonPack(params Quality?[] files) =>
        Candidate(MediaType.Series, Q(Quality.Bluray1080p), Profile(MediaType.Series, Quality.Bluray1080p.Id, upgradeAllowed: true)) with
        {
            FullSeason = true,
            Episodes = [.. files.Select((q, i) => Episode(i + 1, file: q is null ? null : new ExistingFile(Q(q))))],
        };

    [Fact]
    public void RejectsSeasonPackWhenModeIsAllAndNotAllAreUpgradable()
    {
        Assert.False(Accepted(SeasonPack(Quality.Sdtv, Quality.Bluray1080p), new DecisionOptions { SeasonPackUpgrade = SeasonPackUpgradeType.All }));
    }

    [Fact]
    public void RejectsSeasonPackBelowThreshold()
    {
        ReleaseCandidate candidate = SeasonPack(
            Quality.Sdtv, Quality.Sdtv, Quality.Sdtv, Quality.Sdtv, Quality.Sdtv, Quality.Sdtv, Quality.Sdtv, Quality.Bluray1080p, Quality.Bluray1080p, null);

        Rejection? rejection = DownloadSpecifications.UpgradeDisk(candidate, Context(new DecisionOptions { SeasonPackUpgrade = SeasonPackUpgradeType.Threshold, SeasonPackUpgradeThreshold = 90 }));

        Assert.Equal(DownloadRejectionReason.DiskNotUpgrade, rejection?.Reason);
    }

    [Fact]
    public void AcceptsSeasonPackWhenModeIsAnyAndOneIsUpgradable()
    {
        Assert.True(Accepted(SeasonPack(Quality.Sdtv, Quality.Bluray1080p), new DecisionOptions { SeasonPackUpgrade = SeasonPackUpgradeType.Any }));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void UpgradeAllowedRejectsBetterQualityWhenUpgradesAreDisabled(MediaType mediaType)
    {
        ReleaseCandidate candidate = Single(mediaType, Q(Quality.Dvd), Q(Quality.Bluray1080p), Profile(mediaType, Quality.Bluray1080p.Id));

        Assert.Equal(DownloadRejectionReason.QualityUpgradesDisabled, DownloadSpecifications.UpgradeAllowed(candidate, Context())?.Reason);
        Assert.Null(DownloadSpecifications.UpgradeAllowed(candidate with { Profile = candidate.Profile with { UpgradeAllowed = true } }, Context()));
    }
}
