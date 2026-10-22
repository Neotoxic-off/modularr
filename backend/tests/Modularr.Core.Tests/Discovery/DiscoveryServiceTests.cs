using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Discovery;
using Modularr.Core.Languages;
using Modularr.Core.Metadata;
using Modularr.Core.Persistence;
using Modularr.Core.Tests.Library;
using Modularr.Core.Tests.Metadata;
using Modularr.Core.Tests.Persistence;

namespace Modularr.Core.Tests.Discovery;

public sealed class DiscoveryServiceTests : IDisposable
{
    private const string RadarrApi = "https://api.radarr.video/v1/";
    private const string SkyHook = "https://skyhook.sonarr.tv/v2/tvdb/";

    private readonly TestDatabase _database = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose()
    {
        _cache.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task MovieListIsDedupedFlaggedAndCached()
    {
        Movie owned = _database.AddMovie(969681);
        StubHttpHandler http = new StubHttpHandler().Fixture(RadarrApi + "list/tmdb/trending", "radarr-list-tmdb-trending.json");
        DiscoveryService discovery = Service(http);

        DiscoveryResult first = await discovery.DiscoverMoviesAsync(MovieList.Trending, TestContext.Current.CancellationToken);
        DiscoveryResult second = await discovery.DiscoverMoviesAsync(MovieList.Trending, TestContext.Current.CancellationToken);

        Assert.Null(first.Error);
        Assert.Equal([969681, 1003596, 1058424], first.Items.Select(i => i.TmdbId!.Value));
        DiscoveryItem spiderMan = first.Items[0];
        Assert.Equal((owned.Id, true, MediaType.Movie), (spiderMan.LibraryId!.Value, spiderMan.InLibrary, spiderMan.Kind));
        Assert.NotNull(spiderMan.Backdrop);
        Assert.NotNull(spiderMan.Rating);
        Assert.Contains("Action", spiderMan.Genres);
        Assert.False(first.Items[1].InLibrary);
        Assert.Equal(first.Items, second.Items);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task UnavailableSourceGivesAnEmptyListAndItsError()
    {
        StubHttpHandler http = new StubHttpHandler().Respond("https://", HttpStatusCode.BadGateway);

        DiscoveryResult result = await Service(http).DiscoverSeriesAsync(SeriesList.Airing, TestContext.Current.CancellationToken);

        Assert.Empty(result.Items);
        Assert.Contains("502", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UpcomingKeepsMoviesNotOutAnywhereSoonestFirst()
    {
        DateTime now = new(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);
        MovieInfo[] movies =
        [
            Movie(1, inCinemas: now.AddDays(60)),
            Movie(2, inCinemas: now.AddDays(-30), digital: now.AddDays(10)),
            Movie(3, digital: now.AddDays(5)),
            Movie(4),
            Movie(1, inCinemas: now.AddDays(60)),
        ];

        Assert.Equal([3, 1], DiscoveryService.Upcoming(movies, now).Select(m => m.TmdbId!.Value));
    }

    [Fact]
    public async Task LookupAllMergesMoviesAndSeriesAndFlagsTheLibrary()
    {
        Series owned = _database.AddSeries(71663);
        StubHttpHandler http = new StubHttpHandler()
            .Fixture(RadarrApi + "search?", "radarr-search-prometheus.json")
            .Fixture(SkyHook + "search/", "skyhook-search-the-simpsons.json");

        IReadOnlyList<DiscoveryItem> items = await Service(http).LookupAsync("The  Simpsons!", includeMovies: true, includeSeries: true, TestContext.Current.CancellationToken);

        Assert.Equal(("The Simpsons", MediaType.Series, owned.Id), (items[0].Title, items[0].Kind, items[0].LibraryId!.Value));
        Assert.Contains(items, i => i.Kind == MediaType.Movie);
        Assert.Contains(http.Requests, r => r.StartsWith(SkyHook + "search/?language=eng&term=the%20simpsons", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LookupAllSurvivesOneSourceDown()
    {
        StubHttpHandler http = new StubHttpHandler()
            .Fixture(RadarrApi + "search?", "radarr-search-prometheus.json")
            .Respond(SkyHook, HttpStatusCode.ServiceUnavailable);

        IReadOnlyList<DiscoveryItem> items = await Service(http).LookupAsync("prometheus 2012", includeMovies: true, includeSeries: true, TestContext.Current.CancellationToken);

        Assert.Equal((70981, 2012), (items[0].TmdbId!.Value, items[0].Year!.Value));
    }

    [Fact]
    public async Task LookupByTvdbIdOnlyAsksTheSeriesSource()
    {
        StubHttpHandler http = new StubHttpHandler().Fixture(SkyHook + "shows/eng/76885/official", "skyhook-show-76885.json");

        IReadOnlyList<DiscoveryItem> items = await Service(http).LookupAsync("tvdb:76885", includeMovies: true, includeSeries: true, TestContext.Current.CancellationToken);

        Assert.Equal(("Cowboy Bebop", MediaType.Anime), (Assert.Single(items).Title, items[0].Kind));
        Assert.DoesNotContain(http.Requests, r => r.StartsWith(RadarrApi, StringComparison.Ordinal));
    }

    [Fact]
    public async Task LookupTooShortAsksNobody()
    {
        StubHttpHandler http = new();

        Assert.Empty(await Service(http).LookupAsync(" a ", includeMovies: true, includeSeries: true, TestContext.Current.CancellationToken));
        Assert.Empty(http.Requests);
    }

    private DiscoveryService Service(StubHttpHandler http) => Create(new HttpClient(http), _cache, _database.NewContext());

    // The services on one client; the TVmaze index reads without pausing.
    internal static DiscoveryService Create(HttpClient client, IMemoryCache cache, ModularrDbContext db, string? tmdbKey = null, ShowIndex? index = null)
    {
        IOptions<ModularrOptions> options = Options.Create(new ModularrOptions());
        ShowDiscoveryClient shows = new(client, options);
        return new DiscoveryService(
            new MovieMetadataClient(client, options),
            new SeriesMetadataClient(client, options),
            shows,
            new TmdbClient(client, new FixedOptionsMonitor(new ModularrOptions { Metadata = new MetadataOptions { TmdbApiKey = tmdbKey } })),
            index ?? new ShowIndex(shows.GetShowIndexPageAsync, TimeSpan.Zero, CancellationToken.None),
            cache,
            db);
    }

    private static MovieInfo Movie(int tmdbId, DateTime? inCinemas = null, DateTime? digital = null) => new()
    {
        TmdbId = tmdbId,
        Title = $"Movie {tmdbId}",
        OriginalLanguage = Language.English,
        InCinemas = inCinemas,
        DigitalRelease = digital,
    };
}
