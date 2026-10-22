using Modularr.Core.Persistence;
using Modularr.Core.Search;
using Modularr.Core.Tests.Persistence;

namespace Modularr.Core.Tests.Search;

public sealed class AutoSearchTests
{
    private static readonly DateOnly Today = new(2026, 6, 1);

    [Fact]
    public void AWholeMissingSeasonIsOneSeasonSearch()
    {
        Series series = Show((1, 1, null, Today.AddDays(-14)), (1, 2, null, Today.AddDays(-7)), (1, 3, null, Today.AddDays(7)));

        IReadOnlyList<(ReleaseSearchRequest Request, IReadOnlySet<(int Season, int Episode)> Wanted)> plan = AutoSearch.Plan(series, new HashSet<int>(), season: null, Today);

        (ReleaseSearchRequest request, IReadOnlySet<(int, int)> wanted) = Assert.Single(plan);
        Assert.Equal(ReleaseSearchRequest.ForSeason(42, 1), request);
        Assert.Equal([(1, 1), (1, 2)], wanted.Order());
    }

    [Fact]
    public void APartlyMissingSeasonIsSearchedByEpisode()
    {
        // Episode 1 has a file, 2 is downloading, 3 is missing, 4 is not monitored, season 2 is not monitored, specials neither.
        Series series = Show((1, 1, 10, Today), (1, 2, null, Today), (1, 3, null, Today), (1, 4, null, Today), (2, 1, null, Today), (0, 1, null, Today));
        series.Episodes.Single(e => e is { SeasonNumber: 1, EpisodeNumber: 4 }).Monitored = false;
        series.Seasons.Single(s => s.Number == 2).Monitored = false;
        HashSet<int> downloading = [series.Episodes.Single(e => e is { SeasonNumber: 1, EpisodeNumber: 2 }).Id];

        IReadOnlyList<(ReleaseSearchRequest Request, IReadOnlySet<(int Season, int Episode)> Wanted)> plan = AutoSearch.Plan(series, downloading, season: null, Today);

        Assert.Equal([new ReleaseSearchRequest { TvdbId = 42, Season = 1, Episode = "3" }], plan.Select(p => p.Request));
    }

    [Fact]
    public void ASeasonSearchKeepsToThatSeason()
    {
        Series series = Show((1, 1, null, Today), (2, 1, null, Today));

        Assert.Equal([ReleaseSearchRequest.ForSeason(42, 2)], AutoSearch.Plan(series, new HashSet<int>(), season: 2, Today).Select(p => p.Request));
    }

    [Theory]
    [InlineData(TrackedDownloadState.Downloading)]
    [InlineData(TrackedDownloadState.ImportBlocked)]
    [InlineData(TrackedDownloadState.ImportPending)]
    [InlineData(TrackedDownloadState.Importing)]
    [InlineData(TrackedDownloadState.FailedPending)]
    public async Task AMovieWithADownloadInProgressIsNotSearchedAgain(TrackedDownloadState state)
    {
        using TestDatabase database = new();
        Movie movie = database.AddMovie(603);
        database.Insert(new TrackedDownload { InfoHash = "AB", MovieId = movie.Id, ReleaseTitle = "x", State = state });
        using ModularrDbContext db = database.NewContext();

        // No indexer search or grab is reachable: the in-progress check returns first.
        Assert.Empty(await new AutoSearch(db, null!, null!).SearchMovieAsync(movie.Id, TestContext.Current.CancellationToken));
    }

    private static Series Show(params (int Season, int Episode, int? FileId, DateOnly AirDate)[] episodes)
    {
        Series series = new() { TvdbId = 42, Title = "Show", SortTitle = "show", Path = "/tv/show", Monitored = true };
        int id = 0;
        foreach ((int season, int episode, int? fileId, DateOnly airDate) in episodes)
        {
            series.Episodes.Add(new Episode { Id = ++id, SeasonNumber = season, EpisodeNumber = episode, MediaFileId = fileId, AirDate = airDate, Monitored = season > 0 });
        }

        foreach (int season in episodes.Select(e => e.Season).Distinct())
        {
            series.Seasons.Add(new Season { Number = season, Monitored = season > 0 });
        }

        return series;
    }
}
