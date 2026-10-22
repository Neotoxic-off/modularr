using Microsoft.Extensions.Caching.Memory;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Languages;
using Modularr.Core.Library;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;
using Modularr.Core.Subtitles;
using Modularr.Core.Tests.Persistence;
using Modularr.Core.Tests.PostProcessing;

namespace Modularr.Core.Tests.Library;

public sealed class DashboardServiceTests : IDisposable
{
    // A Thursday: its week runs from Monday the 19th to Sunday the 25th.
    private static readonly DateOnly Today = new(2026, 10, 22);

    private readonly TestDatabase _database = new();
    private readonly TempDownload _library = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10 });

    public void Dispose()
    {
        _cache.Dispose();
        _database.Dispose();
        _library.Dispose();
    }

    [Fact]
    public async Task SummarisesTheLibraryInOneCall()
    {
        string moviePath = _library.CreateFile(Path.Combine("movies", "Heat (1995)", "Heat.mkv"));
        _library.CreateFile(Path.Combine("movies", "Heat (1995)", "Heat.fr.srt"));
        string otherPath = _library.CreateFile(Path.Combine("movies", "Alien (1979)", "Alien.mkv"));
        Movie heat = _database.AddMovie(1);
        Movie alien = _database.AddMovie(2);
        _database.Insert(new Movie { TmdbId = 3, Title = "Missing", Path = "/movies/3", QualityProfileId = 1, Monitored = true });
        _database.Insert(File(moviePath, 100, movieId: heat.Id), File(otherPath, 50, movieId: alien.Id));

        Series show = _database.Insert(new Series
        {
            TvdbId = 10, Title = "Show", SortTitle = "show", Path = _library.PathOf("tv"), QualityProfileId = 1, Monitored = true, Poster = "https://img/p.jpg",
        })[0];
        _database.Insert(new Series { TvdbId = 11, Title = "Anime", SortTitle = "anime", Path = _library.PathOf("anime"), QualityProfileId = 1, SeriesType = SeriesType.Anime });
        MediaFile episodeFile = _database.Insert(File(_library.PathOf(Path.Combine("tv", "s01e01.mkv")), 25, seriesId: show.Id))[0];
        _database.Insert(
            new Episode { SeriesId = show.Id, SeasonNumber = 1, EpisodeNumber = 1, AirDate = Today.AddDays(-30), Monitored = true, MediaFileId = episodeFile.Id },
            new Episode { SeriesId = show.Id, SeasonNumber = 1, EpisodeNumber = 2, AirDate = Today.AddDays(-10), Monitored = true },
            new Episode { SeriesId = show.Id, SeasonNumber = 1, EpisodeNumber = 3, AirDate = Today.AddDays(1), Monitored = true, Title = "Next" },
            new Episode { SeriesId = show.Id, SeasonNumber = 0, EpisodeNumber = 1, AirDate = Today.AddDays(-3), Monitored = false });
        _database.Insert([.. Enumerable.Range(1, 12).Select(i => new Activity { Kind = ActivityKind.Added, Level = ActivityLevel.Info, Title = $"A{i}", Time = DateTime.UtcNow })]);

        DashboardSummary summary = await Service(new ModularrOptions
        {
            Paths = new PathOptions { Movies = _library.PathOf("movies"), Series = _library.PathOf("tv"), Anime = _library.PathOf("anime"), Downloads = _library.PathOf("downloads") },
            Media = new MediaOptions { Movies = new MediaDefaults { Subtitles = [Language.French] } },
        }).GetAsync(Today, TestContext.Current.CancellationToken);

        Assert.Equal(new DashboardCounts(Movies: 3, Series: 1, Anime: 1, Episodes: 3, EpisodesWithFile: 1, Missing: 2), summary.Counts);
        Assert.Equal(175, summary.Storage.LibraryBytes);
        Assert.True(summary.Storage.TotalBytes > 0 && summary.Storage.FreeBytes > 0);
        Assert.Equal(summary.Storage.TotalBytes - summary.Storage.FreeBytes, summary.Storage.UsedBytes);
        Assert.Equal(new DashboardSubtitles(Covered: 1, Total: 2), summary.Subtitles);

        // This week: the special aired Monday, the next episode airs Friday.
        Assert.Equal([(0, 1, false, false), (1, 3, false, true)], summary.Airing.Select(a => (a.SeasonNumber, a.EpisodeNumber, a.HasFile, a.Monitored)));
        Assert.Equal(("Show", "https://img/p.jpg", "Next"), (summary.Airing[1].SeriesTitle, summary.Airing[1].Poster, summary.Airing[1].Title));
        Assert.Equal([.. Enumerable.Range(3, 10).Reverse().Select(i => $"A{i}")], summary.Recent.Select(a => a.Title));
    }

    [Fact]
    public async Task AnEmptyLibraryCountsNothing()
    {
        DashboardSummary summary = await Service(new ModularrOptions()).GetAsync(Today, TestContext.Current.CancellationToken);

        Assert.Equal(new DashboardCounts(0, 0, 0, 0, 0, 0), summary.Counts);
        Assert.Equal((0, 0), (summary.Storage.LibraryBytes, summary.Subtitles.Total));
        Assert.Empty(summary.Airing);
    }

    private DashboardService Service(ModularrOptions settings)
    {
        ModularrDbContext db = _database.NewContext();
        FixedOptionsMonitor options = new(settings);
        return new DashboardService(db, new SubtitleService(db, new ActivityLog(db, new ActivityFeed()), options, []), options, _cache);
    }

    private static MediaFile File(string path, long size, int? movieId = null, int? seriesId = null) =>
        new() { MovieId = movieId, SeriesId = seriesId, Path = path, Size = size, Quality = new QualityModel(Quality.Bluray1080p, new Revision()) };
}
