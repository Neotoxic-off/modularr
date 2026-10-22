using Modularr.Core.Decisions;
using Modularr.Core.Indexers;
using Modularr.Core.Parsing.Episodes;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;
using Modularr.Core.Search;

namespace Modularr.Core.Tests.Search;

public sealed class ReleaseDecisionsTests
{
    private const long Megabyte = 1024 * 1024;

    private static readonly QualityProfile Any = DefaultQualityProfiles.Create()[0];
    private static readonly DecisionContext Context = new();

    [Fact]
    public void SeriesReleaseIsMappedToItsLibraryEpisodesAndApproved()
    {
        Series series = Office();

        DownloadDecision decision = ReleaseDecisions.ForSeries(Release("The.Office.US.S01E02.720p.HDTV.x264-GRP", 400), Any, series, Files(), Context);

        Assert.True(decision.Approved, string.Join("; ", decision.Rejections));
        Assert.Equal(MediaType.Series, decision.Candidate.MediaType);
        Assert.Same(Quality.Hdtv720p, decision.Candidate.Quality.Quality);
        EpisodeTarget episode = Assert.Single(decision.Candidate.Episodes);
        Assert.Equal((1, 2, null), (episode.SeasonNumber, episode.EpisodeNumber, episode.File));
        Assert.Equal((1, 1), (decision.Candidate.Pilot!.SeasonNumber, decision.Candidate.Pilot.EpisodeNumber));
    }

    [Fact]
    public void ExistingFileOfTheEpisodeIsComparedForUpgrade()
    {
        DownloadDecision decision = ReleaseDecisions.ForSeries(Release("The.Office.US.S01E01.480p.WEB-DL.x264-GRP", 200), Any, Office(), Files(), Context);

        Assert.Equal(Quality.Hdtv720p, Assert.Single(decision.Candidate.Episodes).File!.Quality.Quality);
        // The default profiles do not allow upgrades, so any existing file meets the cutoff (upstream behaviour).
        Assert.Contains(decision.Rejections, r => r.Reason == DownloadRejectionReason.DiskCutoffMet);
    }

    [Fact]
    public void ReleaseOfAnotherSeriesIsRejected()
    {
        DownloadDecision decision = ReleaseDecisions.ForSeries(Release("Parks.and.Recreation.S01E01.720p.HDTV.x264-GRP", 400), Any, Office(), Files(), Context);

        Assert.Equal(DownloadRejectionReason.UnknownSeries, Assert.Single(decision.Rejections).Reason);
    }

    [Fact]
    public void ReleaseOfAnEpisodeNotInTheLibraryIsRejectedEvenWithoutASize()
    {
        DownloadDecision decision = ReleaseDecisions.ForSeries(Release("The.Office.US.S09E99.720p.HDTV.x264-GRP", null), Any, Office(), Files(), Context);

        Assert.Equal(DownloadRejectionReason.UnknownEpisode, Assert.Single(decision.Rejections).Reason);
    }

    [Theory]
    [InlineData("The.Office.US.S01E02.720p.HDTV.x264-GRP", 1, 2, null)]
    [InlineData("The.Office.US.S02E01.720p.HDTV.x264-GRP", 1, 2, DownloadRejectionReason.WrongSeason)]
    [InlineData("The.Office.US.S01E01.720p.HDTV.x264-GRP", 1, 2, DownloadRejectionReason.WrongEpisode)]
    [InlineData("The.Office.US.S01.720p.HDTV.x264-GRP", 1, 2, DownloadRejectionReason.FullSeason)]
    [InlineData("The.Office.US.S01.720p.HDTV.x264-GRP", 1, null, null)]
    [InlineData("The.Office.US.S02E01.720p.HDTV.x264-GRP", 1, null, DownloadRejectionReason.WrongSeason)]
    public void InteractiveSearchOnlyApprovesTheRequestedSeasonOrEpisode(string title, int season, int? episode, DownloadRejectionReason? rejection)
    {
        DownloadDecision decision = ReleaseDecisions.ForSeries(Release(title, null), Any, Office(), Files(), Context, season, episode);

        // A matching season pack still meets the other specifications (unaired, disk cutoff), only the search match is checked here.
        DownloadRejectionReason[] searchMatch = [DownloadRejectionReason.WrongSeason, DownloadRejectionReason.WrongEpisode, DownloadRejectionReason.FullSeason];
        Assert.Equal(rejection, decision.Rejections.Where(r => searchMatch.Contains(r.Reason)).Select(r => (DownloadRejectionReason?)r.Reason).SingleOrDefault());
    }

    [Fact]
    public void UnparsableReleaseIsRejected()
    {
        DownloadDecision decision = ReleaseDecisions.ForSeries(Release("Some random words", 400), Any, null, Files(), Context);

        Assert.Equal(DownloadRejectionReason.UnableToParse, Assert.Single(decision.Rejections).Reason);
    }

    [Fact]
    public void SeriesNotInTheLibraryTakesEpisodesFromTheTitle()
    {
        DownloadDecision decision = ReleaseDecisions.ForSeries(Release("Some.Show.S02E03E04.1080p.WEB-DL.x264-GRP", 2000), Any, null, Files(), Context);

        Assert.Equal([(2, 3), (2, 4)], decision.Candidate.Episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber)));
    }

    [Theory]
    [InlineData("Inception.2010.1080p.BluRay.x264-GRP", null)]
    [InlineData("Interstellar.2014.1080p.BluRay.x264-GRP", DownloadRejectionReason.WrongMovie)]
    [InlineData("Inception.2014.1080p.BluRay.x264-GRP", DownloadRejectionReason.WrongMovie)]
    [InlineData("Inception.2011.1080p.BluRay.x264-GRP", null)]
    public void MovieReleaseMustMatchTitleAndYear(string title, DownloadRejectionReason? rejection)
    {
        Movie movie = new() { Id = 1, TmdbId = 27205, Title = "Inception", Year = 2010, Runtime = 148, Path = "/movies/Inception (2010)" };

        DownloadDecision decision = ReleaseDecisions.ForMovie(Release(title, 8000), Any, movie, [], Context);

        Assert.Equal(rejection, decision.Rejections.Select(r => (DownloadRejectionReason?)r.Reason).SingleOrDefault());
    }

    [Fact]
    public void FullSeasonMapsEveryEpisodeOfTheSeason()
    {
        ParsedEpisodeInfo parsed = EpisodeParser.ParseTitle("The.Office.US.S01.720p.HDTV.x264-GRP")!;

        Assert.Equal([1, 2], ReleaseDecisions.MapEpisodes(parsed, Office().Episodes).Select(e => e.EpisodeNumber));
    }

    private static Series Office()
    {
        Series series = new() { Id = 1, TvdbId = 73244, Title = "The Office (US)", SortTitle = "office us", Runtime = 22, Path = "/tv/The Office (US)" };
        series.Episodes.Add(new Episode { Id = 1, SeasonNumber = 1, EpisodeNumber = 1, MediaFileId = 7 });
        series.Episodes.Add(new Episode { Id = 2, SeasonNumber = 1, EpisodeNumber = 2 });
        series.Episodes.Add(new Episode { Id = 3, SeasonNumber = 2, EpisodeNumber = 1 });
        return series;
    }

    private static Dictionary<int, MediaFile> Files() => new()
    {
        [7] = new MediaFile { Id = 7, SeriesId = 1, Path = "/tv/The Office (US)/Season 01/e1.mkv", Quality = new QualityModel(Quality.Hdtv720p, new Revision()) },
    };

    private static ReleaseInfo Release(string title, long? megabytes) => new()
    {
        Id = title,
        Title = title,
        Indexer = "Test",
        MagnetUrl = "magnet:?xt=urn:btih:0000000000000000000000000000000000000000",
        Size = megabytes * Megabyte,
        Seeders = 10,
        PublishDate = DateTimeOffset.UtcNow.AddDays(-1),
    };
}
