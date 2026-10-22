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
            Anime = new MediaDefaults { ProfileId = 2, Language = Language.Original },
        },
    };

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AMovieWithoutProfileGetsTheMoviesDefaultAndKeepsItsLanguages()
    {
        using ModularrDbContext db = _database.NewContext();

        Movie movie = await Library(db).AddMovieAsync(70981, qualityProfileId: null, Language.French, search: false, TestContext.Current.CancellationToken);

        Assert.Equal((4, Language.English, Language.French), (movie.QualityProfileId, movie.OriginalLanguage, movie.Language));
        Assert.Equal(Language.French, Settings.Media.WantedLanguage(movie));
    }

    [Fact]
    public async Task AnAnimeWithoutProfileGetsTheAnimeDefault()
    {
        using ModularrDbContext db = _database.NewContext();

        Series series = await Library(db).AddSeriesAsync(76885, qualityProfileId: null, language: null, SeriesMonitor.All, search: false, TestContext.Current.CancellationToken);

        Assert.Equal((MediaType.Anime, 2, Language.Japanese), (series.MediaType, series.QualityProfileId, series.OriginalLanguage));
        Assert.Equal(Language.Original, Settings.Media.WantedLanguage(series));
    }

    [Fact]
    public async Task AnExplicitProfileMustExist()
    {
        using ModularrDbContext db = _database.NewContext();

        await Assert.ThrowsAsync<ArgumentException>(() => Library(db).AddMovieAsync(70981, 42, null, search: false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PatchLeavesAbsentFieldsAndClearsANullLanguage()
    {
        Movie movie = _database.Insert(new Movie { TmdbId = 1, Title = "M", Path = "/movies/m", QualityProfileId = 1, Monitored = true, Language = Language.French })[0];
        using ModularrDbContext db = _database.NewContext();
        LibraryService library = Library(db);
        CancellationToken ct = TestContext.Current.CancellationToken;

        Movie kept = await library.UpdateMovieAsync(movie.Id, Patch("""{ "qualityProfileId": 5 }"""), ct);
        Assert.Equal((5, Language.French, true), (kept.QualityProfileId, kept.Language, kept.Monitored));

        Movie cleared = await library.UpdateMovieAsync(movie.Id, Patch("""{ "language": null, "monitored": false }"""), ct);
        Assert.Equal((5, null, false), (cleared.QualityProfileId, cleared.Language, cleared.Monitored));

        Movie set = await library.UpdateMovieAsync(movie.Id, Patch("""{ "language": "Original" }"""), ct);
        Assert.Equal(Language.Original, set.Language);
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
    public async Task UnknownIsRejectedAsAnItemLanguage()
    {
        Movie movie = _database.AddMovie(1);
        using ModularrDbContext db = _database.NewContext();

        await Assert.ThrowsAsync<ArgumentException>(() => Library(db).UpdateMovieAsync(movie.Id, Patch("""{ "language": "Unknown" }"""), TestContext.Current.CancellationToken));
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
        Movie movie = await Library(db, searches).AddMovieAsync(70981, qualityProfileId: null, language: null, search: false, ct);
        Series series = await Library(db, searches).AddSeriesAsync(76885, qualityProfileId: null, language: null, SeriesMonitor.All, search: true, ct);

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
