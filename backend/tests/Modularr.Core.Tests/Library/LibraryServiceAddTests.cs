using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Catalog;
using Modularr.Core.Languages;
using Modularr.Core.Library;
using Modularr.Core.Metadata;
using Modularr.Core.Persistence;
using Modularr.Core.Search;
using Modularr.Core.Tests.Metadata;
using Modularr.Core.Tests.Persistence;

namespace Modularr.Core.Tests.Library;

public sealed class LibraryServiceAddTests : IDisposable
{
    private readonly TestDatabase _database = new();

    private readonly StubHttpHandler _http = new StubHttpHandler()
        .Fixture("https://api.radarr.video/v1/movie/70981", "radarr-movie-70981.json")
        .Fixture("https://skyhook.sonarr.tv/v2/tvdb/shows/eng/76885/official", "skyhook-show-76885.json")
        .Respond("https://services.sonarr.tv/v1/dailyseries", HttpStatusCode.OK, "[]");

    private static readonly ModularrOptions Settings = new()
    {
        Paths = new PathOptions { Series = "/tv", Movies = "/movies", Anime = "/anime" },
        Media = new MediaOptions
        {
            Movies = new MediaDefaults { ProfileId = 4 },
            Series = new MediaDefaults { ProfileId = 3 },
            Anime = new MediaDefaults { ProfileId = 2, Version = ReleaseVersion.VOSTFR, Subtitles = [Language.French] },
        },
    };

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AMovieWithoutProfileGetsTheMoviesDefaultAndKeepsItsLanguages()
    {
        using ModularrDbContext db = _database.NewContext();

        Movie movie = await Library(db).AddMovieAsync(70981, qualityProfileId: null, ReleaseVersion.VF, search: false, TestContext.Current.CancellationToken);

        Assert.Equal((4, Language.English, ReleaseVersion.VF), (movie.QualityProfileId, movie.OriginalLanguage, movie.Version));
        Assert.Equal(ReleaseVersion.VF, Settings.Media.WantedVersion(movie));
    }

    [Fact]
    public async Task AnAnimeWithoutProfileGetsTheAnimeDefault()
    {
        using ModularrDbContext db = _database.NewContext();

        Series series = await Library(db).AddSeriesAsync(76885, qualityProfileId: null, version: null, SeriesMonitor.All, seasons: null, search: false, TestContext.Current.CancellationToken);

        Assert.Equal((MediaType.Anime, 2, Language.Japanese), (series.MediaType, series.QualityProfileId, series.OriginalLanguage));
        Assert.Equal(ReleaseVersion.VOSTFR, Settings.Media.WantedVersion(series));
        Assert.Equal([Language.French], Settings.Media.WantedSubtitles(series));
    }

    [Fact]
    public async Task AnExplicitProfileMustExist()
    {
        using ModularrDbContext db = _database.NewContext();

        await Assert.ThrowsAsync<ArgumentException>(() => Library(db).AddMovieAsync(70981, 42, null, search: false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PatchLeavesAbsentFieldsAndClearsANullOverride()
    {
        Movie movie = _database.Insert(new Movie { TmdbId = 1, Title = "M", Path = "/movies/m", QualityProfileId = 1, Monitored = true, Version = ReleaseVersion.VF })[0];
        using ModularrDbContext db = _database.NewContext();
        LibraryService library = Library(db);
        CancellationToken ct = TestContext.Current.CancellationToken;

        Movie kept = await library.UpdateMovieAsync(movie.Id, Patch("""{ "qualityProfileId": 5 }"""), ct);
        Assert.Equal((5, ReleaseVersion.VF, true), (kept.QualityProfileId, kept.Version, kept.Monitored));

        Movie cleared = await library.UpdateMovieAsync(movie.Id, Patch("""{ "version": null, "monitored": false }"""), ct);
        Assert.Equal((5, null, false), (cleared.QualityProfileId, cleared.Version, cleared.Monitored));

        Movie set = await library.UpdateMovieAsync(movie.Id, Patch("""{ "version": "MULTi", "subtitles": ["French", "English"] }"""), ct);
        Assert.Equal(ReleaseVersion.Multi, set.Version);
        Assert.Equal([Language.French, Language.English], set.Subtitles);

        await Assert.ThrowsAsync<ArgumentException>(() => library.UpdateMovieAsync(movie.Id, Patch("""{ "subtitles": ["Original"] }"""), ct));
    }

    [Fact]
    public async Task BackfillFillsItemsAddedWithoutMetadataAndSkipsTheOnesTheSourceLost()
    {
        // As the MediaPreferences migration leaves them: no original language, no genres.
        Movie known = _database.Insert(new Movie { TmdbId = 70981, Title = "Prometheus", Path = "/movies/p", QualityProfileId = 1 })[0];
        Movie lost = _database.Insert(new Movie { TmdbId = 1, Title = "Lost", Path = "/movies/l", QualityProfileId = 1 })[0];
        using (ModularrDbContext db = _database.NewContext())
        {
            Assert.Equal(1, await Library(db).BackfillMetadataAsync(TestContext.Current.CancellationToken));
        }

        using ModularrDbContext check = _database.NewContext();
        Movie filled = check.Movies.Single(m => m.Id == known.Id);
        Assert.Equal(Language.English, filled.OriginalLanguage);
        Assert.NotEmpty(filled.Genres);
        Assert.Equal(("Prometheus", "/movies/p"), (filled.Title, filled.Path));
        Assert.Equal(Language.Unknown, check.Movies.Single(m => m.Id == lost.Id).OriginalLanguage);
    }

    [Fact]
    public async Task BackfillFillsTheReleaseDaysOfMoviesAddedBeforeTheCalendar()
    {
        // As the CalendarDates migration leaves them: metadata known, no release day.
        Movie movie = _database.Insert(new Movie { TmdbId = 70981, Title = "Prometheus", Path = "/movies/p", QualityProfileId = 1, OriginalLanguage = Language.English })[0];
        using (ModularrDbContext db = _database.NewContext())
        {
            Assert.Equal(1, await Library(db).BackfillMetadataAsync(TestContext.Current.CancellationToken));
        }

        using ModularrDbContext check = _database.NewContext();
        Movie filled = check.Movies.Single(m => m.Id == movie.Id);
        Assert.Equal(
            (new DateOnly(2012, 5, 30), new DateOnly(2012, 6, 7), new DateOnly(2012, 9, 25)),
            (filled.InCinemas, filled.DigitalRelease, filled.PhysicalRelease));
    }

    [Fact]
    public async Task UnknownIsNotAVersion()
    {
        Movie movie = _database.AddMovie(1);
        using ModularrDbContext db = _database.NewContext();

        await Assert.ThrowsAsync<ArgumentException>(() => Library(db).UpdateMovieAsync(movie.Id, Patch("""{ "version": "Unknown" }"""), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(SeriesMonitor.All, true, new[] { false, true, true })]
    [InlineData(SeriesMonitor.Future, true, new[] { false, false, true })]
    [InlineData(SeriesMonitor.None, false, new[] { false, false, false })]
    public void MonitorChoosesTheEpisodes(SeriesMonitor monitor, bool seriesMonitored, bool[] episodesMonitored)
    {
        SeriesInfo info = new()
        {
            TvdbId = 1,
            Title = "Show",
            OriginalLanguage = Language.English,
            Seasons = [new SeasonInfo(0, null, []), new SeasonInfo(1, null, [])],
            Episodes =
            [
                new EpisodeInfo { TvdbId = 1, SeasonNumber = 0, EpisodeNumber = 1, AirDate = new DateOnly(2030, 1, 1) },
                new EpisodeInfo { TvdbId = 2, SeasonNumber = 1, EpisodeNumber = 1, AirDate = new DateOnly(2020, 1, 1) },
                new EpisodeInfo { TvdbId = 3, SeasonNumber = 1, EpisodeNumber = 2, AirDate = new DateOnly(2030, 1, 1) },
            ],
        };

        Series series = LibraryService.ToSeries(info, 1, Settings.Paths, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), monitor);

        Assert.Equal(seriesMonitored, series.Monitored);
        Assert.Equal(episodesMonitored, series.Episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).Select(e => e.Monitored));
    }

    [Theory]
    [InlineData(SeriesMonitor.Latest, null, new[] { 3 })]
    [InlineData(SeriesMonitor.First, null, new[] { 1 })]
    [InlineData(SeriesMonitor.Custom, new[] { 0, 2 }, new[] { 0, 2 })]
    [InlineData(SeriesMonitor.Custom, new int[0], new int[0])]
    public void MonitorPicksTheSeasons(SeriesMonitor monitor, int[]? seasons, int[] expected)
    {
        Series series = LibraryService.ToSeries(FourSeasons(), 1, Settings.Paths, DateTime.UnixEpoch, monitor, seasons);

        Assert.Equal(expected, series.Seasons.Where(s => s.Monitored).Select(s => s.Number).Order());
        Assert.Equal(expected, series.Episodes.Where(e => e.Monitored).Select(e => e.SeasonNumber).Order());
    }

    [Fact]
    public void CustomMonitoringNeedsTheSeasons() =>
        Assert.Throws<ArgumentException>(() => LibraryService.ToSeries(FourSeasons(), 1, Settings.Paths, DateTime.UnixEpoch, SeriesMonitor.Custom));

    [Fact]
    public async Task SeasonMonitoringAppliesToItsEpisodes()
    {
        Series added = _database.Insert(LibraryService.ToSeries(FourSeasons(), 1, Settings.Paths, DateTime.UnixEpoch, SeriesMonitor.None))[0];
        using ModularrDbContext db = _database.NewContext();

        Series series = await Library(db).SetSeasonMonitoredAsync(added.Id, 2, monitored: true, TestContext.Current.CancellationToken);

        Assert.Equal([2], series.Seasons.Where(s => s.Monitored).Select(s => s.Number));
        using ModularrDbContext check = _database.NewContext();
        Assert.Equal([2], check.Episodes.Where(e => e.Monitored).Select(e => e.SeasonNumber));
        Assert.True(check.Series.Single().Seasons.Single(s => s.Number == 2).Monitored);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Library(db).SetSeasonMonitoredAsync(added.Id, 9, monitored: true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MonitoringEpisodesMonitorsTheirSeason()
    {
        Series added = _database.Insert(LibraryService.ToSeries(FourSeasons(), 1, Settings.Paths, DateTime.UnixEpoch, SeriesMonitor.None))[0];
        int[] ids = [.. added.Episodes.Where(e => e.SeasonNumber is 1 or 3).Select(e => e.Id)];
        using ModularrDbContext db = _database.NewContext();

        IReadOnlyList<Episode> episodes = await Library(db).SetEpisodesMonitoredAsync(ids, monitored: true, TestContext.Current.CancellationToken);

        Assert.All(episodes, e => Assert.True(e.Monitored));
        using ModularrDbContext check = _database.NewContext();
        Assert.Equal([1, 3], check.Series.Single().Seasons.Where(s => s.Monitored).Select(s => s.Number).Order());
        await Library(check).SetEpisodesMonitoredAsync([ids[0]], monitored: false, TestContext.Current.CancellationToken);
        Assert.Equal([3], check.Episodes.Where(e => e.Monitored).Select(e => e.SeasonNumber));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Library(check).SetEpisodesMonitoredAsync([ids[0], 999], monitored: true, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => Library(check).SetEpisodesMonitoredAsync([], monitored: true, TestContext.Current.CancellationToken));
    }

    // Specials and three seasons of one aired episode each.
    private static SeriesInfo FourSeasons() => new()
    {
        TvdbId = 7,
        Title = "Four Seasons",
        OriginalLanguage = Language.English,
        Seasons = [.. Enumerable.Range(0, 4).Select(n => new SeasonInfo(n, null, []))],
        Episodes = [.. Enumerable.Range(0, 4).Select(n => new EpisodeInfo { TvdbId = 100 + n, SeasonNumber = n, EpisodeNumber = 1, AirDate = new DateOnly(2020, 1, 1) })],
    };

    private static ItemUpdate Patch(string json) => JsonSerializer.Deserialize<ItemUpdate>(json, JsonSerializerOptions.Web)!;

    [Fact]
    public async Task AddingSearchesTheAiredMissingEpisodesAtOnceUnlessTheClientOptsOut()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using ServiceProvider provider = SearchServices();
        using SearchRunner searches = new(provider.GetRequiredService<IServiceScopeFactory>());
        await searches.StartAsync(ct);
        using ModularrDbContext db = _database.NewContext();

        // The test database is one connection: wait on the feed, not by reading it while the search runs.
        await using IAsyncEnumerator<ServerEvent?> events = provider.GetRequiredService<ActivityFeed>().SubscribeAsync(TimeSpan.FromMinutes(1), ct).GetAsyncEnumerator(ct);
        ValueTask<bool> subscribed = events.MoveNextAsync();

        // Jobs run in order: had the movie been queued, it would be searched before the series.
        Movie movie = await Library(db, searches).AddMovieAsync(70981, qualityProfileId: null, version: null, search: false, ct);
        Series series = await Library(db, searches).AddSeriesAsync(76885, qualityProfileId: null, version: null, SeriesMonitor.All, seasons: null, search: true, ct);

        Assert.True(await subscribed);
        while (events.Current?.Data is not Activity { Kind: ActivityKind.NothingFound })
        {
            Assert.True(await events.MoveNextAsync());
        }

        await searches.StopAsync(ct);
        List<Activity> activities = await db.Activities.OrderBy(a => a.Id).ToListAsync(ct);
        Assert.Equal(ActivityKind.Added, activities.Single(a => a.MovieId == movie.Id).Kind);
        Activity[] seriesActivities = [.. activities.Where(a => a.SeriesId == series.Id)];
        Assert.Equal([ActivityKind.Added, ActivityKind.SearchStarted, ActivityKind.NothingFound], seriesActivities.Select(a => a.Kind));
        Assert.All(seriesActivities, a => Assert.Equal(series.Title, a.Title));
        int missing = series.Episodes.Count(e => e.Monitored && e.AirDate <= DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.True(missing > 0);
        Assert.Equal($"Searching for {missing} missing episode(s)", seriesActivities[1].Message);
        Assert.Equal("0 release(s) found, 0 approved, nothing grabbed", seriesActivities[2].Message);
    }

    // The real search pipeline over no indexer: every search finds nothing, and no grab is reachable.
    private ServiceProvider SearchServices()
    {
        IOptions<ModularrOptions> options = Options.Create(Settings);
        return new ServiceCollection()
            .AddScoped(_ => _database.NewContext())
            .AddSingleton<ActivityFeed>()
            .AddScoped<ActivityLog>()
            .AddScoped(services =>
            {
                ModularrDbContext db = services.GetRequiredService<ModularrDbContext>();
                ReleaseSearch search = new(db, new IndexerCatalogHost(options, null!), new IndexerSearch(null!, null!, options), new FixedOptionsMonitor(Settings));
                return new AutoSearch(db, search, null!, services.GetRequiredService<ActivityLog>());
            })
            .BuildServiceProvider();
    }

    private LibraryService Library(ModularrDbContext db, SearchRunner? searches = null)
    {
        IOptions<ModularrOptions> metadataOptions = Options.Create(new ModularrOptions());
        return new LibraryService(
            db,
            new SeriesMetadataClient(new HttpClient(_http), metadataOptions),
            new MovieMetadataClient(new HttpClient(_http), metadataOptions),
            new FixedOptionsMonitor(Settings),
            searches ?? new SearchRunner(null!),
            new ActivityLog(db, new ActivityFeed()));
    }
}
