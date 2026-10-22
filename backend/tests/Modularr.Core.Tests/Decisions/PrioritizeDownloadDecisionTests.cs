// Ported from Sonarr and Radarr DecisionEngineTests/PrioritizeDownloadDecisionFixture. Releases are torrents:
// upstream Usenet-only cases (protocol preference, youngest first) are skipped, age-based cases rely on size and seeders here.
using Modularr.Core.Decisions;
using Modularr.Core.Indexers;
using Modularr.Core.Qualities;
using static Modularr.Core.Tests.Decisions.DecisionTestData;

namespace Modularr.Core.Tests.Decisions;

public sealed class PrioritizeDownloadDecisionTests
{
    private static DownloadDecision Episode(QualityModel quality, long size = 0, int seeders = 0, int peers = 0, int indexerPriority = 25, MediaType mediaType = MediaType.Series, int[]? episodes = null) =>
        new(Candidate(mediaType, quality, Profile(mediaType)) with
        {
            Release = Release(size: size) with { Seeders = seeders, Peers = peers, IndexerPriority = indexerPriority },
            Runtime = 60,
            Episodes = mediaType == MediaType.Movie ? [] : [.. (episodes ?? [1]).Select(n => DecisionTestData.Episode(n))],
        },
        []);

    private static DownloadDecision Movie(QualityModel quality, long size = 0, int runtime = 150) =>
        new(Candidate(MediaType.Movie, quality, Profile(MediaType.Movie)) with { Release = Release(size: size), Runtime = runtime }, []);

    private static IReadOnlyList<DownloadDecision> Prioritize(DecisionContext context, params DownloadDecision[] decisions) =>
        DownloadDecisionMaker.Prioritize(decisions, context);

    private static IReadOnlyList<DownloadDecision> Prioritize(params DownloadDecision[] decisions) => Prioritize(NoPreferredSize(), decisions);

    private static DecisionContext PreferredSize(double? megabytesPerMinute) => Context() with
    {
        QualityDefinitions = Quality.All.ToDictionary(q => q, q => new QualityDefinition(q, null, null, megabytesPerMinute)),
    };

    private static DecisionContext NoPreferredSize() => PreferredSize(null);

    public static TheoryData<MediaType> MediaTypes => [MediaType.Series, MediaType.Movie];

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void PutsRealsBeforeNonReals(MediaType mediaType)
    {
        IReadOnlyList<DownloadDecision> result = Prioritize(Episode(Q(Quality.Hdtv720p, 1, 0), mediaType: mediaType), Episode(Q(Quality.Hdtv720p, 1, 1), mediaType: mediaType));

        Assert.Equal(1, result[0].Candidate.Quality.Revision.Real);
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void PutsPropersBeforeNonPropers(MediaType mediaType)
    {
        IReadOnlyList<DownloadDecision> result = Prioritize(Episode(Q(Quality.Hdtv720p, 1), mediaType: mediaType), Episode(Q(Quality.Hdtv720p, 2), mediaType: mediaType));

        Assert.Equal(2, result[0].Candidate.Quality.Revision.Version);
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void PutsHigherQualityBeforeLower(MediaType mediaType)
    {
        IReadOnlyList<DownloadDecision> result = Prioritize(Episode(Q(Quality.Sdtv), mediaType: mediaType), Episode(Q(Quality.Hdtv720p), mediaType: mediaType));

        Assert.Equal(Quality.Hdtv720p, result[0].Candidate.Quality.Quality);
    }

    [Fact]
    public void OrdersByLowestEpisodeNumber()
    {
        IReadOnlyList<DownloadDecision> result = Prioritize(Episode(Q(Quality.Hdtv720p), episodes: [2]), Episode(Q(Quality.Hdtv720p), episodes: [1]));

        Assert.Equal(1, result[0].Candidate.Episodes[0].EpisodeNumber);
    }

    [Fact]
    public void OrdersByLowestEpisodeNumberWithMultipleEpisodes()
    {
        IReadOnlyList<DownloadDecision> result = Prioritize(Episode(Q(Quality.Hdtv720p), episodes: [2, 3]), Episode(Q(Quality.Hdtv720p), episodes: [1, 2]));

        Assert.Equal(1, result[0].Candidate.Episodes[0].EpisodeNumber);
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void OrdersByLargestRoundedTo200Megabytes(MediaType mediaType)
    {
        DownloadDecision largest = Episode(Q(Quality.Hdtv720p), 3000 * Megabyte, mediaType: mediaType);

        IReadOnlyList<DownloadDecision> result = Prioritize(
            Episode(Q(Quality.Sdtv), 100 * Megabyte, mediaType: mediaType),
            Episode(Q(Quality.Hdtv720p), 1200 * Megabyte, mediaType: mediaType),
            Episode(Q(Quality.Hdtv720p), 1250 * Megabyte, mediaType: mediaType),
            largest);

        Assert.Same(largest, result[0]);
    }

    [Fact]
    public void OrdersByClosestToPreferredSizeWhenBothAreUnder()
    {
        // 200 MB/min * 60 min = 12000 MB.
        DownloadDecision large = Episode(Q(Quality.Hdtv720p), 10000 * Megabyte);

        Assert.Same(large, Prioritize(PreferredSize(200), Episode(Q(Quality.Hdtv720p), 1200 * Megabyte), large)[0]);
    }

    [Fact]
    public void OrdersByClosestToPreferredSizeWhenPreferredIsInBetween()
    {
        // 46 MB/min * 60 min = 2760 MB.
        DownloadDecision closest = Episode(Q(Quality.Hdtv720p), 3000 * Megabyte);

        IReadOnlyList<DownloadDecision> result = Prioritize(
            PreferredSize(46),
            Episode(Q(Quality.Hdtv720p), 500 * Megabyte),
            Episode(Q(Quality.Hdtv720p), 2000 * Megabyte),
            closest,
            Episode(Q(Quality.Hdtv720p), 5000 * Megabyte));

        Assert.Same(closest, result[0]);
    }

    [Fact]
    public void OrdersByClosestToPreferredSizeOfTheWholeRelease()
    {
        // 46 MB/min * 10 episodes * 60 min = 27600 MB.
        int[] episodes = [.. Enumerable.Range(1, 10)];
        DownloadDecision small = Episode(Q(Quality.Hdtv720p), 12000 * Megabyte, episodes: episodes);
        DownloadDecision large = Episode(Q(Quality.Hdtv720p), 26000 * Megabyte, episodes: episodes);

        Assert.Same(large, Prioritize(PreferredSize(46), small, large)[0]);
    }

    [Fact]
    public void DoesNotThrowWithoutEpisodes()
    {
        DownloadDecision withoutEpisodes = new(Episode(Q(Quality.Hdtv720p), 500 * Megabyte).Candidate with { Episodes = [] }, []);

        Assert.Equal(2, Prioritize(withoutEpisodes, Episode(Q(Quality.Hdtv720p), 500 * Megabyte)).Count);
    }

    [Fact]
    public void PrefersSeasonPackOverSingleEpisode()
    {
        DownloadDecision seasonPack = new(Episode(Q(Quality.Hdtv720p), episodes: [1, 2]).Candidate with { FullSeason = true }, []);

        Assert.Same(seasonPack, Prioritize(seasonPack, Episode(Q(Quality.Hdtv720p), episodes: [1]))[0]);
    }

    [Fact]
    public void PrefersMultiEpisodeOverSingleEpisodeForAnime()
    {
        IReadOnlyList<DownloadDecision> result = Prioritize(
            Episode(Q(Quality.Hdtv720p), mediaType: MediaType.Anime, episodes: [1, 2]),
            Episode(Q(Quality.Hdtv720p), mediaType: MediaType.Anime, episodes: [1]));

        Assert.Equal(2, result[0].Candidate.Episodes.Count);
    }

    [Fact]
    public void PrefersSingleEpisodeOverMultiEpisodeForNonAnime()
    {
        IReadOnlyList<DownloadDecision> result = Prioritize(Episode(Q(Quality.Hdtv720p), episodes: [1, 2]), Episode(Q(Quality.Hdtv720p), episodes: [1]));

        Assert.Single(result[0].Candidate.Episodes);
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void PrefersMoreSeeders(MediaType mediaType)
    {
        IReadOnlyList<DownloadDecision> result = Prioritize(Episode(Q(Quality.Hdtv720p), seeders: 10, mediaType: mediaType), Episode(Q(Quality.Hdtv720p), seeders: 100, mediaType: mediaType));

        Assert.Equal(100, result[0].Candidate.Release.Seeders);
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void PrefersMorePeersGivenEqualSeeders(MediaType mediaType)
    {
        IReadOnlyList<DownloadDecision> result = Prioritize(
            Episode(Q(Quality.Hdtv720p), seeders: 10, peers: 10, mediaType: mediaType),
            Episode(Q(Quality.Hdtv720p), seeders: 10, peers: 100, mediaType: mediaType));

        Assert.Equal(100, result[0].Candidate.Release.Peers);
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void PrefersMorePeersWithoutSeeders(MediaType mediaType)
    {
        IReadOnlyList<DownloadDecision> result = Prioritize(
            Episode(Q(Quality.Hdtv720p), seeders: 0, peers: 10, mediaType: mediaType),
            Episode(Q(Quality.Hdtv720p), seeders: 0, peers: 100, mediaType: mediaType));

        Assert.Equal(100, result[0].Candidate.Release.Peers);
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void PrefersFirstReleaseWhenPeersAndSizeAreTooSimilar(MediaType mediaType)
    {
        DownloadDecision first = Episode(Q(Quality.Hdtv720p), 250 * Megabyte, seeders: 1000, peers: 10, mediaType: mediaType);

        Assert.Same(first, Prioritize(first, Episode(Q(Quality.Hdtv720p), 200 * Megabyte, seeders: 1100, peers: 10, mediaType: mediaType))[0]);
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void PrefersFirstReleaseWhenAgeAndSizeAreTooSimilar(MediaType mediaType)
    {
        DownloadDecision first = Episode(Q(Quality.Hdtv720p), 200 * Megabyte, mediaType: mediaType);
        DownloadDecision older = Episode(Q(Quality.Hdtv720p), 250 * Megabyte, mediaType: mediaType);
        first = first with { Candidate = first.Candidate with { Release = first.Candidate.Release with { PublishDate = DateTimeOffset.UtcNow.AddDays(-100) } } };
        older = older with { Candidate = older.Candidate with { Release = older.Candidate.Release with { PublishDate = DateTimeOffset.UtcNow.AddDays(-150) } } };

        Assert.Same(first, Prioritize(first, older)[0]);
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void PrefersQualityOverPeers(MediaType mediaType)
    {
        DownloadDecision better = Episode(Q(Quality.Bluray1080p), 250 * Megabyte, seeders: 100, peers: 10, mediaType: mediaType);

        Assert.Same(better, Prioritize(better, Episode(Q(Quality.Sdtv), 200 * Megabyte, seeders: 1100, peers: 10, mediaType: mediaType))[0]);
    }

    [Theory]
    [InlineData(ProperDownloadType.PreferAndUpgrade, 2)]
    [InlineData(ProperDownloadType.DoNotUpgrade, 2)]
    [InlineData(ProperDownloadType.DoNotPrefer, 1)]
    public void PropersRankOnlyWhenPreferred(ProperDownloadType propers, int expectedVersion)
    {
        // DoNotPrefer: the tie falls through to later steps and the first release stays first (upstream: the custom format score decides).
        DecisionContext context = NoPreferredSize() with { Options = new DecisionOptions { DownloadPropersAndRepacks = propers } };

        IReadOnlyList<DownloadDecision> result = Prioritize(context, Episode(Q(Quality.WebDl1080p, 1)), Episode(Q(Quality.WebDl1080p, 2)));

        Assert.Equal(expectedVersion, result[0].Candidate.Quality.Revision.Version);
    }

    [Fact]
    public void RealsDoNotRankWhenPropersAreNotPreferred()
    {
        DecisionContext context = NoPreferredSize() with { Options = new DecisionOptions { DownloadPropersAndRepacks = ProperDownloadType.DoNotPrefer } };

        IReadOnlyList<DownloadDecision> result = Prioritize(context, Episode(Q(Quality.WebDl1080p, 1, 0)), Episode(Q(Quality.WebDl1080p, 1, 1)));

        Assert.Equal(0, result[0].Candidate.Quality.Revision.Real);
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void SortsByIndexerPriority(MediaType mediaType)
    {
        DownloadDecision first = Episode(Q(Quality.WebDl1080p), indexerPriority: 25, mediaType: mediaType);
        DownloadDecision second = Episode(Q(Quality.WebDl1080p), indexerPriority: 50, mediaType: mediaType);
        DownloadDecision third = Episode(Q(Quality.WebDl1080p), indexerPriority: 1, mediaType: mediaType);

        Assert.Equal(new[] { third, first, second }, Prioritize(first, second, third));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void IndexerPriorityDoesNotBeatQuality(MediaType mediaType)
    {
        DownloadDecision first = Episode(Q(Quality.Hdtv720p), indexerPriority: 25, mediaType: mediaType);
        DownloadDecision second = Episode(Q(Quality.WebDl1080p), indexerPriority: 50, mediaType: mediaType);
        DownloadDecision third = Episode(Q(Quality.Sdtv), indexerPriority: 1, mediaType: mediaType);
        DownloadDecision fourth = Episode(Q(Quality.WebDl1080p), indexerPriority: 25, mediaType: mediaType);

        Assert.Equal(new[] { fourth, second, first, third }, Prioritize(first, second, third, fourth));
    }

    [Fact]
    public void MovieOrdersByClosestToPreferredSizeWhenBothAreOver()
    {
        // 2 MB/min * 150 min = 300 MB.
        DownloadDecision small = Movie(Q(Quality.Hdtv720p), 400 * Megabyte);

        Assert.Same(small, Prioritize(PreferredSize(2), small, Movie(Q(Quality.Hdtv720p), 15000 * Megabyte))[0]);
    }

    [Fact]
    public void MovieOrdersByLargestWithoutRuntime()
    {
        DownloadDecision large = Movie(Q(Quality.Hdtv720p), 15000 * Megabyte, runtime: 0);

        Assert.Same(large, Prioritize(PreferredSize(2), Movie(Q(Quality.Hdtv720p), 400 * Megabyte, runtime: 0), large)[0]);
    }

    [Fact]
    public void MovieOrdersByClosestToPreferredSizeWhenBothAreUnder()
    {
        // 390 MB/min * 150 min = 58500 MB.
        DownloadDecision large = Movie(Q(Quality.Hdtv720p), 15000 * Megabyte);

        Assert.Same(large, Prioritize(PreferredSize(390), Movie(Q(Quality.Hdtv720p), 100 * Megabyte), large)[0]);
    }

    [Fact]
    public void MovieOrdersByClosestToPreferredSizeWhenPreferredIsInBetween()
    {
        // 46 MB/min * 150 min = 6900 MB.
        DownloadDecision closest = Movie(Q(Quality.Hdtv720p), 7000 * Megabyte);

        IReadOnlyList<DownloadDecision> result = Prioritize(
            PreferredSize(46),
            Movie(Q(Quality.Hdtv720p), 100 * Megabyte),
            Movie(Q(Quality.Hdtv720p), 500 * Megabyte),
            closest,
            Movie(Q(Quality.Hdtv720p), 15000 * Megabyte));

        Assert.Same(closest, result[0]);
    }

    [Fact]
    public void ReleaseInfoDefaultsToMiddleIndexerPriority()
    {
        Assert.Equal(25, new ReleaseInfo { Id = "1", Title = "t", Indexer = "i" }.IndexerPriority);
    }
}
