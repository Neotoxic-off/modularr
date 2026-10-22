using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Downloads;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;
using Modularr.Core.Subtitles;
using Modularr.Core.Tests.Persistence;
using Modularr.Core.Tests.PostProcessing;

namespace Modularr.Core.Tests.Downloads;

public sealed class DownloadImporterTests : IDisposable
{
    // Never started: what the importer queues is not fetched.
    private static readonly SubtitleRunner Subtitles = new(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

    private readonly TestDatabase _database = new();
    private readonly TempDownload _download = new();
    private readonly TempDownload _library = new();

    [Fact]
    public async Task SeasonPackIsLinkedIntoTheSeriesAndRecorded()
    {
        Series series = new() { TvdbId = 73244, Title = "The Office (US)", SortTitle = "office us", Path = _library.PathOf("The Office (US)"), QualityProfileId = 1 };
        series.Episodes.Add(new Episode { SeasonNumber = 1, EpisodeNumber = 1 });
        series.Episodes.Add(new Episode { SeasonNumber = 1, EpisodeNumber = 2 });
        _database.Insert(series);
        string pack = _download.PathOf("The.Office.US.S01.720p.HDTV.x264-GRP");
        _download.CreateFile("The.Office.US.S01.720p.HDTV.x264-GRP/The.Office.US.S01E01.720p.HDTV.x264-GRP.mkv");
        _download.CreateFile("The.Office.US.S01.720p.HDTV.x264-GRP/The.Office.US.S01E02.720p.HDTV.x264-GRP.mkv");
        _download.CreateFile("The.Office.US.S01.720p.HDTV.x264-GRP/The.Office.US.S01E01.720p.HDTV.x264-GRP.en.srt");
        _download.CreateFile("The.Office.US.S01.720p.HDTV.x264-GRP/release.nfo");
        TrackedDownload tracked = Track(new TrackedDownload { InfoHash = "AA", SeriesId = series.Id, ReleaseTitle = "The.Office.US.S01.720p.HDTV.x264-GRP" });

        bool remove = await Import(tracked, pack);

        Assert.False(remove);
        string season = Path.Combine(series.Path, "Season 01");
        Assert.True(File.Exists(Path.Combine(season, "The.Office.US.S01E01.720p.HDTV.x264-GRP.mkv")));
        Assert.True(File.Exists(Path.Combine(season, "The.Office.US.S01E02.720p.HDTV.x264-GRP.mkv")));
        Assert.True(File.Exists(Path.Combine(season, "The.Office.US.S01E01.720p.HDTV.x264-GRP.en.srt")));
        Assert.True(File.Exists(Path.Combine(pack, "The.Office.US.S01E01.720p.HDTV.x264-GRP.mkv")), "the torrent keeps its data to seed");

        using ModularrDbContext db = _database.NewContext();
        Assert.Equal(TrackedDownloadState.Imported, db.TrackedDownloads.Single().State);
        List<MediaFile> files = [.. db.MediaFiles.OrderBy(f => f.Path)];
        Assert.Equal(2, files.Count);
        Assert.All(files, f => Assert.Equal(Quality.Hdtv720p, f.Quality.Quality));
        Assert.Equal(files.Select(f => (int?)f.Id), db.Episodes.OrderBy(e => e.EpisodeNumber).Select(e => e.MediaFileId));
        Assert.Equal(2, db.History.Count(h => h.EventType == HistoryEventType.Imported && h.EpisodeId != null));
    }

    [Fact]
    public async Task SingleMovieFileIsImported()
    {
        Movie movie = _database.Insert(new Movie { TmdbId = 27205, Title = "Inception", Year = 2010, Path = _library.PathOf("Inception (2010)"), QualityProfileId = 1 })[0];
        string file = _download.CreateFile("Inception.2010.1080p.BluRay.x264-GRP.mkv");
        TrackedDownload tracked = Track(new TrackedDownload { InfoHash = "BB", MovieId = movie.Id, ReleaseTitle = "Inception.2010.1080p.BluRay.x264-GRP" });

        Assert.False(await Import(tracked, file));

        using ModularrDbContext db = _database.NewContext();
        MediaFile imported = db.MediaFiles.Single();
        Assert.Equal(Path.Combine(movie.Path, "Inception.2010.1080p.BluRay.x264-GRP.mkv"), imported.Path);
        Assert.Equal((movie.Id, Quality.Bluray1080p, "GRP"), (imported.MovieId!.Value, imported.Quality.Quality, imported.ReleaseGroup));
        Assert.Equal(HistoryEventType.Imported, db.History.Single().EventType);
        Activity activity = db.Activities.Single();
        Assert.Equal((ActivityKind.Imported, tracked.Id, movie.Id), (activity.Kind, activity.DownloadId!.Value, activity.MovieId!.Value));
        Assert.Equal($"Imported to {imported.Path}", activity.Message);
    }

    [Fact]
    public async Task DangerousReleaseIsBlocklistedAndRemoved()
    {
        Movie movie = _database.AddMovie(1);
        string folder = _download.PathOf("Movie.1.2020.1080p.WEB-DL-GRP");
        _download.CreateFile("Movie.1.2020.1080p.WEB-DL-GRP/movie.mkv");
        _download.CreateFile("Movie.1.2020.1080p.WEB-DL-GRP/codec.exe");
        TrackedDownload tracked = Track(new TrackedDownload { InfoHash = "CC", MovieId = movie.Id, ReleaseTitle = "Movie.1.2020.1080p.WEB-DL-GRP", Indexer = "YTS" });

        Assert.True(await Import(tracked, folder));

        using ModularrDbContext db = _database.NewContext();
        BlocklistItem blocked = db.Blocklist.Single();
        Assert.Equal(("CC", "YTS", movie.Id), (blocked.InfoHash, blocked.Indexer, blocked.MovieId!.Value));
        Assert.Contains("executable", blocked.Reason, StringComparison.Ordinal);
        Assert.Equal(TrackedDownloadState.Failed, db.TrackedDownloads.Single().State);
        Assert.Equal(HistoryEventType.Failed, db.History.Single().EventType);
        Assert.Empty(db.MediaFiles);
        Assert.Equal([ActivityKind.Blocklisted, ActivityKind.DownloadFailed], db.Activities.OrderBy(a => a.Id).Select(a => a.Kind));
    }

    [Fact]
    public async Task VideoMatchingNoEpisodeBlocksTheImport()
    {
        Series series = _database.AddSeries(1);
        string file = _download.CreateFile("Other.Show.S05E05.720p.HDTV.x264-GRP.mkv");
        TrackedDownload tracked = Track(new TrackedDownload { InfoHash = "DD", SeriesId = series.Id, ReleaseTitle = "Other.Show.S05E05.720p.HDTV.x264-GRP" });

        Assert.False(await Import(tracked, file));

        using ModularrDbContext db = _database.NewContext();
        Assert.Equal(TrackedDownloadState.ImportBlocked, db.TrackedDownloads.Single().State);
        Assert.Empty(db.MediaFiles);
        Assert.Equal((ActivityKind.ImportFailed, ActivityLevel.Error), db.Activities.Select(a => new ValueTuple<ActivityKind, ActivityLevel>(a.Kind, a.Level)).Single());
    }

    public void Dispose()
    {
        _database.Dispose();
        _download.Dispose();
        _library.Dispose();
    }

    private TrackedDownload Track(TrackedDownload download) => _database.Insert(download)[0];

    private async Task<bool> Import(TrackedDownload tracked, string outputPath)
    {
        using ModularrDbContext db = _database.NewContext();
        TrackedDownload download = await db.TrackedDownloads.SingleAsync(d => d.Id == tracked.Id, TestContext.Current.CancellationToken);
        return await new DownloadImporter(db, new ActivityLog(db, new ActivityFeed()), Options.Create(new ModularrOptions()), Subtitles).ImportAsync(download, outputPath, TestContext.Current.CancellationToken);
    }
}
