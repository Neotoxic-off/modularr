// Ported from Sonarr and Radarr DecisionEngineTests/AcceptableSizeSpecificationFixture.
// Sonarr sets the limits on profile items, Radarr on quality definitions: both come from the definitions here.
using Modularr.Core.Decisions;
using Modularr.Core.Qualities;
using static Modularr.Core.Tests.Decisions.DecisionTestData;

namespace Modularr.Core.Tests.Decisions;

public sealed class AcceptableSizeSpecificationTests
{
    private static readonly DateTimeOffset PilotAirDate = new(2024, 1, 1, 20, 0, 0, TimeSpan.Zero);

    private static readonly QualityProfile SeriesProfile = new()
    {
        Cutoff = Quality.Sdtv.Id,
        Items =
        [
            new QualityProfileQualityItem { Quality = Quality.Sdtv },
            new QualityProfileQualityItem { Quality = Quality.RawHd },
            new QualityProfileQualityItem
            {
                Name = "WEB 720p",
                Items = [new QualityProfileQualityItem { Quality = Quality.WebDl720p }, new QualityProfileQualityItem { Quality = Quality.WebRip720p }],
            },
        ],
    };

    private static DecisionContext SizeContext(double? sdtvMin = 2, double? sdtvMax = 10) => new()
    {
        QualityDefinitions = new Dictionary<Quality, QualityDefinition>
        {
            [Quality.Sdtv] = new(Quality.Sdtv, sdtvMin, sdtvMax, null),
            [Quality.RawHd] = new(Quality.RawHd, 2, null, null),
            [Quality.WebDl720p] = new(Quality.WebDl720p, 2, 20, null),
            [Quality.WebRip720p] = new(Quality.WebRip720p, 2, 20, null),
        },
    };

    private static ReleaseCandidate Series(int seriesRuntime, long sizeInMegabytes, params EpisodeTarget[] episodes) => new()
    {
        Release = Release(size: sizeInMegabytes * Megabyte),
        MediaType = MediaType.Series,
        Profile = SeriesProfile,
        Quality = Q(Quality.Sdtv, 2),
        Runtime = seriesRuntime,
        Episodes = episodes,
        Pilot = new EpisodeTarget { SeasonNumber = 1, EpisodeNumber = 1, Runtime = 30, AirDate = PilotAirDate },
    };

    private static EpisodeTarget[] Episodes(int count, int runtime, int seasonNumber = 1, DateTimeOffset? airDate = null) =>
        [.. Enumerable.Range(1, count).Select(n => new EpisodeTarget { SeasonNumber = seasonNumber, EpisodeNumber = n, Runtime = runtime, AirDate = airDate })];

    private static bool Accepted(ReleaseCandidate candidate, DecisionContext? context = null) =>
        DownloadSpecifications.AcceptableSize(candidate, context ?? SizeContext()) is null;

    [Theory]
    [InlineData(30, 50, false)]
    [InlineData(30, 250, true)]
    [InlineData(30, 500, false)]
    [InlineData(60, 100, false)]
    [InlineData(60, 500, true)]
    [InlineData(60, 1000, false)]
    public void SingleEpisode(int runtime, int sizeInMegabytes, bool expected)
    {
        Assert.Equal(expected, Accepted(Series(runtime, sizeInMegabytes, Episodes(1, runtime))));
    }

    // Sonarr first/last episode of season cases: the episode keeps its own 30 minute runtime whatever the series runtime.
    [Theory]
    [InlineData(30, 250, true)]
    [InlineData(30, 1000, false)]
    [InlineData(60, 250, true)]
    [InlineData(60, 2000, false)]
    public void UsesTheEpisodeRuntimeForFirstAndLastEpisodeOfSeason(int seriesRuntime, int sizeInMegabytes, bool expected)
    {
        Assert.Equal(expected, Accepted(Series(seriesRuntime, sizeInMegabytes, Episodes(1, 30))));
    }

    [Theory]
    [InlineData(30, 50 * 2, false)]
    [InlineData(30, 250 * 2, true)]
    [InlineData(30, 500 * 2, false)]
    [InlineData(60, 100 * 2, false)]
    [InlineData(60, 500 * 2, true)]
    [InlineData(60, 1000 * 2, false)]
    public void MultiEpisode(int runtime, int sizeInMegabytes, bool expected)
    {
        Assert.Equal(expected, Accepted(Series(runtime, sizeInMegabytes, Episodes(2, runtime))));
    }

    [Theory]
    [InlineData(30, 50 * 6, false)]
    [InlineData(30, 250 * 6, true)]
    [InlineData(30, 500 * 6, false)]
    [InlineData(60, 100 * 6, false)]
    [InlineData(60, 500 * 6, true)]
    [InlineData(60, 1000 * 6, false)]
    public void MultiSetEpisode(int runtime, int sizeInMegabytes, bool expected)
    {
        Assert.Equal(expected, Accepted(Series(runtime, sizeInMegabytes, Episodes(6, runtime))));
    }

    [Fact]
    public void AcceptsUnknownSize()
    {
        Assert.True(Accepted(Series(30, 0, Episodes(1, 30)), SizeContext(10, 20)));
    }

    [Theory]
    [InlineData(30, 18457280000)]
    [InlineData(60, 36857280000)]
    public void AcceptsAnySizeWhenUnlimited(int runtime, long size)
    {
        ReleaseCandidate candidate = Series(runtime, 0, Episodes(1, runtime)) with { Release = Release(size: size) };

        Assert.True(Accepted(candidate, SizeContext(2, null)));
    }

    [Fact]
    public void TreatsDailySeriesAsSingleEpisode()
    {
        Assert.True(Accepted(Series(60, 300, Episodes(1, 60))));
    }

    [Fact]
    public void UsesGroupedQualityLimits()
    {
        Assert.True(Accepted(Series(30, 500, Episodes(1, 30)) with { Quality = Q(Quality.WebDl720p) }));
    }

    [Fact]
    public void AcceptsRawHdWithoutMaximum()
    {
        Assert.True(Accepted(Series(45, 8000, Episodes(1, 30)) with { Quality = Q(Quality.RawHd) }));
    }

    [Fact]
    public void AcceptsSpecials()
    {
        Assert.True(Accepted(Series(30, 50, Episodes(1, 30)) with { Special = true }));
    }

    [Fact]
    public void RejectsZeroRuntimeEpisodeNotFromFirstSeason()
    {
        Rejection? rejection = DownloadSpecifications.AcceptableSize(Series(0, 200, Episodes(1, 0, seasonNumber: 2)), SizeContext());

        Assert.Equal(DownloadRejectionReason.UnknownRuntime, rejection?.Reason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RejectsZeroRuntimeEpisodesAiredMoreThan24HoursAfterThePilot(int count)
    {
        Assert.False(Accepted(Series(0, 200, Episodes(count, 0, airDate: PilotAirDate.AddDays(7)))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Uses45MinutesForZeroRuntimeEpisodesAiredWithin24HoursOfThePilot(int count)
    {
        Assert.True(Accepted(Series(0, 200, Episodes(count, 0, airDate: PilotAirDate.AddHours(1)))));
    }

    [Fact]
    public void RejectsZeroRuntimeMultiEpisodeNotFromFirstSeason()
    {
        Assert.False(Accepted(Series(0, 200, Episodes(2, 0, seasonNumber: 2))));
    }

    [Fact]
    public void UsesSeriesRuntimeWhenEpisodeRuntimeIsNotSet()
    {
        Assert.True(Accepted(Series(30, 200, Episodes(1, 0, airDate: PilotAirDate.AddHours(1)))));
    }

    private static ReleaseCandidate Movie(int runtime, long size) => new()
    {
        Release = Release("A.Movie.1998.SDTV-Group", size),
        MediaType = MediaType.Movie,
        Profile = Profile(MediaType.Movie),
        Quality = Q(Quality.Sdtv, 2),
        Runtime = runtime,
    };

    [Theory]
    [InlineData(30, 50, false)]
    [InlineData(30, 250, true)]
    [InlineData(30, 500, false)]
    [InlineData(60, 100, false)]
    [InlineData(60, 500, true)]
    [InlineData(60, 1000, false)]
    public void MovieSize(int runtime, int sizeInMegabytes, bool expected)
    {
        Assert.Equal(expected, Accepted(Movie(runtime, sizeInMegabytes * Megabyte)));
    }

    [Fact]
    public void AcceptsMovieOfUnknownSize()
    {
        Assert.True(Accepted(Movie(120, 0), SizeContext(10, 20)));
    }

    [Theory]
    [InlineData(30, 18457280000)]
    [InlineData(60, 36857280000)]
    public void AcceptsAnyMovieSizeWhenUnlimited(int runtime, long size)
    {
        Assert.True(Accepted(Movie(runtime, size), SizeContext(2, null)));
    }

    [Fact]
    public void Uses110MinutesForMovieWithoutRuntime()
    {
        Assert.True(Accepted(Movie(0, 1095 * Megabyte)));
        Assert.False(Accepted(Movie(0, 1105 * Megabyte)));
    }

    [Fact]
    public void DefaultsDifferBetweenSeriesAndMovies()
    {
        Assert.Equal(2, QualityDefinition.SeriesDefaults[Quality.Sdtv].MinSize);
        Assert.Equal(0, QualityDefinition.MovieDefaults[Quality.Sdtv].MinSize);
        Assert.False(QualityDefinition.SeriesDefaults.ContainsKey(Quality.Cam));
        Assert.Same(QualityDefinition.SeriesDefaults, QualityDefinition.DefaultsFor(MediaType.Anime));
    }
}
