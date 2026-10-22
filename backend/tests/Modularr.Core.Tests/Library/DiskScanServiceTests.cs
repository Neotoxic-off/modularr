using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Library;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;
using Modularr.Core.Tests.Persistence;
using Modularr.Core.Tests.PostProcessing;

namespace Modularr.Core.Tests.Library;

public sealed class DiskScanServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly TempDownload _disk = new();

    public void Dispose()
    {
        _database.Dispose();
        _disk.Dispose();
    }

    private string Full(string relative) => Path.GetFullPath(_disk.PathOf(relative));

    private ModularrOptions Settings => new()
    {
        Paths = new PathOptions { Series = Full("series"), Movies = Full("movies"), Anime = Full("anime") },
    };

    [Fact]
    public async Task FoundFilesAreAddedAndGoneFilesLoseTheirRowOnly()
    {
        Series series = new() { TvdbId = 1, Title = "Show", SortTitle = "show", Path = Full("series/Show"), QualityProfileId = 1 };
        series.Episodes.Add(new Episode { SeasonNumber = 1, EpisodeNumber = 1 });
        series.Episodes.Add(new Episode { SeasonNumber = 1, EpisodeNumber = 2 });
        _database.Insert(series);
        MediaFile gone = _database.Insert(new MediaFile
        {
            SeriesId = series.Id, Path = Full("series/Show/Season 01/Show.S01E02.mkv"), Quality = new QualityModel(Quality.Hdtv720p, new Revision()),
        })[0];
        using (ModularrDbContext setup = _database.NewContext())
        {
            setup.Episodes.Single(e => e.EpisodeNumber == 2).MediaFileId = gone.Id;
            setup.SaveChanges();
        }

        string found = Path.GetFullPath(_disk.CreateFile("series/Show/Season 01/Show.S01E01.1080p.WEB-DL.x264-GRP.mkv"));
        _disk.CreateFile("series/Show/Season 01/Sample/Show.S01E02.sample.mkv");
        _disk.CreateFile("series/Show/Season 01/Show.S01E02.nfo");

        ScanResult result = await Scan(scans => scans.ScanSeriesAsync(series.Id, record: true, TestContext.Current.CancellationToken));

        Assert.Equal(new ScanResult(1, 1, 1), result);
        using ModularrDbContext db = _database.NewContext();
        MediaFile added = db.MediaFiles.Single();
        Assert.Equal((found, Quality.WebDl1080p, "GRP"), (added.Path, added.Quality.Quality, added.ReleaseGroup));
        Assert.Equal([added.Id, null], db.Episodes.OrderBy(e => e.EpisodeNumber).Select(e => e.MediaFileId));
        Assert.True(File.Exists(found));
        Assert.Equal(ActivityKind.ScanCompleted, db.Activities.Single().Kind);
    }

    [Fact]
    public async Task AMovieKeepsItsFileAndGetsTheLargestVideoWhenItHasNone()
    {
        Movie movie = _database.Insert(new Movie { TmdbId = 1, Title = "Heat", Year = 1995, Path = Full("movies/Heat (1995)"), QualityProfileId = 1 })[0];
        string feature = Path.GetFullPath(_disk.CreateFile("movies/Heat (1995)/Heat.1995.1080p.BluRay.x264-GRP.mkv", size: 2048));
        _disk.CreateFile("movies/Heat (1995)/Extras/Heat.Trailer.mkv", size: 1024);

        Assert.Equal(new ScanResult(1, 1, 0), await Scan(scans => scans.ScanMovieAsync(movie.Id, record: true, TestContext.Current.CancellationToken)));
        Assert.Equal(new ScanResult(1, 0, 0), await Scan(scans => scans.ScanMovieAsync(movie.Id, record: true, TestContext.Current.CancellationToken)));

        using ModularrDbContext db = _database.NewContext();
        Assert.Equal((feature, Quality.Bluray1080p), (db.MediaFiles.Single().Path, db.MediaFiles.Single().Quality.Quality));
    }

    [Fact]
    public async Task FoldersOutsideTheRootsOrMissingAreNotScannedAndKeepTheirRows()
    {
        Movie outside = _database.Insert(new Movie { TmdbId = 1, Title = "Out", Path = Full("elsewhere/Out"), QualityProfileId = 1 })[0];
        Movie missing = _database.Insert(new Movie { TmdbId = 2, Title = "Gone", Path = Full("movies/Gone"), QualityProfileId = 1 })[0];
        _disk.CreateFile("elsewhere/Out/Out.2020.mkv");
        _database.Insert(new MediaFile { MovieId = missing.Id, Path = Full("movies/Gone/Gone.mkv"), Quality = new QualityModel(Quality.Hdtv720p, new Revision()) });

        ScanResult result = await Scan(scans => scans.ScanAllAsync(TestContext.Current.CancellationToken));

        Assert.Equal(new ScanResult(0, 0, 0), result);
        using ModularrDbContext db = _database.NewContext();
        Assert.Equal(missing.Id, db.MediaFiles.Single().MovieId);
        Assert.DoesNotContain(db.MediaFiles, f => f.MovieId == outside.Id);
        Assert.Equal("0 title(s) scanned, 0 file(s) found, 0 missing", db.Activities.Single().Message);
    }

    [Fact]
    public async Task LinksAreNotFollowed()
    {
        string target = Path.GetDirectoryName(_disk.CreateFile("outside/Linked.2020.1080p.WEB-DL.mkv"))!;
        Movie inside = _database.Insert(new Movie { TmdbId = 1, Title = "In", Path = Full("movies/In"), QualityProfileId = 1 })[0];
        Movie linked = _database.Insert(new Movie { TmdbId = 2, Title = "Linked", Path = Full("movies/Linked"), QualityProfileId = 1 })[0];
        Directory.CreateDirectory(inside.Path);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(inside.Path, "link"), target);
            Directory.CreateSymbolicLink(linked.Path, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"Symbolic links are not allowed here: {ex.Message}");
        }

        ScanResult result = await Scan(scans => scans.ScanAllAsync(TestContext.Current.CancellationToken));

        Assert.Equal(new ScanResult(1, 0, 0), result);
        using ModularrDbContext db = _database.NewContext();
        Assert.Empty(db.MediaFiles);
    }

    private async Task<ScanResult> Scan(Func<DiskScanService, Task<ScanResult>> scan)
    {
        using ModularrDbContext db = _database.NewContext();
        return await scan(new DiskScanService(db, new ActivityLog(db, new ActivityFeed()), new FixedOptionsMonitor(Settings)));
    }
}
