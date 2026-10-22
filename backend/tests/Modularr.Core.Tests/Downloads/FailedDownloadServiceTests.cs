using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Downloads;
using Modularr.Core.Persistence;
using Modularr.Core.Search;
using Modularr.Core.Subtitles;
using Modularr.Core.Tests.Persistence;

namespace Modularr.Core.Tests.Downloads;

public sealed class FailedDownloadServiceTests : IDisposable
{
    // Never started: what the importer queues is not fetched.
    private static readonly SubtitleRunner Subtitles = new(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

    private const int MaxRegrabs = 3;

    private readonly TestDatabase _database = new();

    [Fact]
    public async Task FailureBlocklistsTheReleaseAndKeepsTheReason()
    {
        Movie movie = _database.AddMovie(603);
        TrackedDownload tracked = _database.Insert(new TrackedDownload
        {
            InfoHash = "AB", MovieId = movie.Id, ReleaseTitle = "The.Matrix.1999.1080p.BluRay.x264-GRP", Indexer = "YTS", State = TrackedDownloadState.Downloading,
        })[0];

        using (ModularrDbContext db = _database.NewContext())
        {
            TrackedDownload download = await db.TrackedDownloads.SingleAsync(TestContext.Current.CancellationToken);
            await new DownloadImporter(db, new ActivityLog(db, new ActivityFeed()), Options.Create(new ModularrOptions()), Subtitles).FailAsync(download, "No seeds or peers for 01:00:00", TestContext.Current.CancellationToken);
        }

        using ModularrDbContext check = _database.NewContext();
        TrackedDownload failed = check.TrackedDownloads.Single(d => d.Id == tracked.Id);
        Assert.Equal((TrackedDownloadState.Failed, "No seeds or peers for 01:00:00"), (failed.State, failed.Message));
        BlocklistItem blocked = check.Blocklist.Single();
        Assert.Equal(("AB", "YTS", movie.Id, "No seeds or peers for 01:00:00"), (blocked.InfoHash, blocked.Indexer, blocked.MovieId!.Value, blocked.Reason));
        HistoryItem history = check.History.Single();
        Assert.Equal((HistoryEventType.Failed, "No seeds or peers for 01:00:00"), (history.EventType, history.Data["reason"]));
        Assert.All(check.Activities, a => Assert.Equal(("No seeds or peers for 01:00:00", tracked.Id), (a.Message, a.DownloadId!.Value)));
        Assert.Equal([ActivityKind.Blocklisted, ActivityKind.DownloadFailed], check.Activities.OrderBy(a => a.Id).Select(a => a.Kind));
    }

    [Fact]
    public async Task MovieIsSearchedAgainByItsId()
    {
        Movie movie = _database.AddMovie(603);

        ReleaseSearchRequest? request = await RegrabRequest(new TrackedDownload { InfoHash = "AA", MovieId = movie.Id, ReleaseTitle = "x" });

        Assert.Equal(new ReleaseSearchRequest { Type = MediaType.Movie, TmdbId = 603 }, request);
    }

    [Fact]
    public async Task EpisodeIsSearchedAgainBySeasonAndEpisode()
    {
        Series series = AddSeries((1, 1), (1, 2), (2, 1));
        int[] episodeIds = [.. series.Episodes.Where(e => e is { SeasonNumber: 1, EpisodeNumber: 2 }).Select(e => e.Id)];

        ReleaseSearchRequest? request = await RegrabRequest(new TrackedDownload { InfoHash = "AA", SeriesId = series.Id, EpisodeIds = episodeIds, ReleaseTitle = "x" });

        Assert.Equal(new ReleaseSearchRequest { TvdbId = series.TvdbId, Season = 1, Episode = "2" }, request);
    }

    [Fact]
    public async Task SeasonPackIsSearchedAgainBySeason()
    {
        Series series = AddSeries((1, 1), (1, 2), (2, 1));
        int[] episodeIds = [.. series.Episodes.Where(e => e.SeasonNumber == 1).Select(e => e.Id)];

        ReleaseSearchRequest? request = await RegrabRequest(new TrackedDownload { InfoHash = "AA", SeriesId = series.Id, EpisodeIds = episodeIds, ReleaseTitle = "x" });

        Assert.Equal(new ReleaseSearchRequest { TvdbId = series.TvdbId, Season = 1 }, request);
    }

    [Fact]
    public async Task UnknownOrMultiSeasonEpisodesAreNotSearchedAgain()
    {
        Series series = AddSeries((1, 1), (2, 1));

        Assert.Null(await RegrabRequest(new TrackedDownload { InfoHash = "AA", SeriesId = series.Id, ReleaseTitle = "x" }));
        Assert.Null(await RegrabRequest(new TrackedDownload { InfoHash = "BB", SeriesId = series.Id, EpisodeIds = [.. series.Episodes.Select(e => e.Id)], ReleaseTitle = "x" }));
    }

    [Fact]
    public async Task ReGrabsStopAtTheCap()
    {
        Movie movie = _database.AddMovie(603);

        Assert.NotNull(await RegrabRequest(new TrackedDownload { InfoHash = "AA", MovieId = movie.Id, ReleaseTitle = "x", Regrabs = MaxRegrabs - 1 }));
        Assert.Null(await RegrabRequest(new TrackedDownload { InfoHash = "BB", MovieId = movie.Id, ReleaseTitle = "x", Regrabs = MaxRegrabs }));
    }

    public void Dispose() => _database.Dispose();

    private Series AddSeries(params (int Season, int Episode)[] episodes)
    {
        Series series = new() { TvdbId = 73244, Title = "The Office (US)", SortTitle = "office us", Path = "/tv/office", QualityProfileId = 1 };
        foreach ((int season, int episode) in episodes)
        {
            series.Episodes.Add(new Episode { SeasonNumber = season, EpisodeNumber = episode });
        }

        return _database.Insert(series)[0];
    }

    private async Task<ReleaseSearchRequest?> RegrabRequest(TrackedDownload download)
    {
        using ModularrDbContext db = _database.NewContext();
        return await FailedDownloadService.RegrabRequestAsync(db, download, MaxRegrabs, TestContext.Current.CancellationToken);
    }
}
