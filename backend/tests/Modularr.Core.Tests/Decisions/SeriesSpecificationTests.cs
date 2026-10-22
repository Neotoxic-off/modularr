// Ported from Sonarr DecisionEngineTests FullSeason, MultiSeason and AnimeVersionUpgrade specification fixtures.
// SplitEpisode has no upstream fixture: its cases are new.
using Modularr.Core.Decisions;
using Modularr.Core.Qualities;
using static Modularr.Core.Tests.Decisions.DecisionTestData;

namespace Modularr.Core.Tests.Decisions;

public sealed class SeriesSpecificationTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static ReleaseCandidate SeasonPack(DateTimeOffset? lastAirDate) => Candidate(MediaType.Series) with
    {
        FullSeason = true,
        Episodes =
        [
            new EpisodeTarget { EpisodeNumber = 1, AirDate = Now.AddDays(-8) },
            new EpisodeTarget { EpisodeNumber = 2, AirDate = Now.AddDays(-8) },
            new EpisodeTarget { EpisodeNumber = 3, AirDate = lastAirDate },
        ],
    };

    private static bool FullSeasonAccepted(ReleaseCandidate candidate, bool allowNotAired = false) =>
        DownloadSpecifications.FullSeason(candidate, Context(new DecisionOptions { AllowSeasonPackWithoutAllEpisodesAired = allowNotAired }) with { Now = Now }) is null;

    [Fact]
    public void AcceptsNotFullSeason()
    {
        Assert.True(FullSeasonAccepted(SeasonPack(Now.AddDays(2)) with { FullSeason = false }));
    }

    [Fact]
    public void AcceptsFullSeasonWhenAllEpisodesHaveAired()
    {
        Assert.True(FullSeasonAccepted(SeasonPack(Now.AddDays(-8))));
    }

    [Fact]
    public void AcceptsFullSeasonWhenAllEpisodesAirWithin24Hours()
    {
        Assert.True(FullSeasonAccepted(SeasonPack(Now.AddHours(23))));
    }

    [Fact]
    public void RejectsFullSeasonWhenAnEpisodeHasNotAired()
    {
        Assert.False(FullSeasonAccepted(SeasonPack(Now.AddDays(2))));
    }

    [Fact]
    public void RejectsFullSeasonWhenAnEpisodeHasNoAirDate()
    {
        Assert.False(FullSeasonAccepted(SeasonPack(null)));
    }

    [Fact]
    public void AcceptsFullSeasonNotAiredWhenAllowed()
    {
        Assert.True(FullSeasonAccepted(SeasonPack(Now.AddDays(2)), allowNotAired: true));
    }

    [Fact]
    public void AcceptsSingleSeason()
    {
        Assert.Null(DownloadSpecifications.MultiSeason(SeasonPack(Now.AddDays(2)), Context()));
    }

    [Fact]
    public void RejectsMultiSeason()
    {
        Assert.Equal(DownloadRejectionReason.MultiSeason, DownloadSpecifications.MultiSeason(SeasonPack(Now) with { MultiSeason = true }, Context())?.Reason);
    }

    [Fact]
    public void RejectsSplitEpisode()
    {
        Assert.Null(DownloadSpecifications.SplitEpisode(Candidate(MediaType.Series), Context()));
        Assert.Equal(DownloadRejectionReason.SplitEpisode, DownloadSpecifications.SplitEpisode(Candidate(MediaType.Series) with { SplitEpisode = true }, Context())?.Reason);
    }

    private static ReleaseCandidate AnimeV2(string? fileReleaseGroup = "DRONE2", string? releaseGroup = "DRONE", MediaType mediaType = MediaType.Anime, int version = 2, bool hasFile = true) =>
        Candidate(mediaType, Q(Quality.Hdtv720p, version)) with
        {
            ReleaseGroup = releaseGroup,
            Episodes = [Episode(file: hasFile ? new ExistingFile(Q(Quality.Hdtv720p), fileReleaseGroup) : null)],
        };

    private static bool AnimeAccepted(ReleaseCandidate candidate, ProperDownloadType propers = ProperDownloadType.PreferAndUpgrade) =>
        DownloadSpecifications.AnimeVersionUpgrade(candidate, Context(new DecisionOptions { DownloadPropersAndRepacks = propers })) is null;

    [Fact]
    public void AnimeAcceptsWhenNoExistingFile()
    {
        Assert.True(AnimeAccepted(AnimeV2(hasFile: false)));
    }

    [Fact]
    public void AnimeRulesSkipStandardSeries()
    {
        Assert.True(AnimeAccepted(AnimeV2(mediaType: MediaType.Series)));
    }

    [Fact]
    public void AnimeAcceptsWhenNotAVersionUpgrade()
    {
        Assert.True(AnimeAccepted(AnimeV2(version: 1)));
    }

    [Fact]
    public void AnimeAcceptsWhenReleaseGroupMatches()
    {
        Assert.True(AnimeAccepted(AnimeV2(fileReleaseGroup: "DRONE")));
    }

    [Fact]
    public void AnimeRejectsWhenExistingFileHasNoReleaseGroup()
    {
        Assert.False(AnimeAccepted(AnimeV2(fileReleaseGroup: "")));
    }

    [Fact]
    public void AnimeRejectsWhenReleaseHasNoReleaseGroup()
    {
        Assert.False(AnimeAccepted(AnimeV2(releaseGroup: "")));
    }

    [Fact]
    public void AnimeRejectsWhenReleaseGroupDoesNotMatch()
    {
        Assert.Equal(DownloadRejectionReason.ReleaseGroupDoesNotMatch, DownloadSpecifications.AnimeVersionUpgrade(AnimeV2(), Context())?.Reason);
    }

    [Fact]
    public void AnimeAcceptsWhenRepacksAreNotPreferred()
    {
        Assert.True(AnimeAccepted(AnimeV2(), ProperDownloadType.DoNotPrefer));
    }
}
