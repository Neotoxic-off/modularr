using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Discovery;
using Modularr.Core.Library;
using Modularr.Core.Metadata;
using Modularr.Core.Persistence;
using Modularr.Core.Search;
using Modularr.Core.Tests.Metadata;
using Modularr.Core.Tests.Persistence;

namespace Modularr.Core.Tests.Library;

public sealed class CollectionServiceTests : IDisposable
{
    private const string RadarrApi = "https://api.radarr.video/v1/";
    private const string TmdbApi = "https://api.themoviedb.org/3/";

    private readonly TestDatabase _database = new();

    private readonly StubHttpHandler _http = new StubHttpHandler()
        .Fixture(RadarrApi + "movie/collection/135416", "radarr-collection-135416.json")
        .Fixture(RadarrApi + "movie/70981", "radarr-movie-70981.json")
        .Respond(RadarrApi + "movie/126889", HttpStatusCode.OK, """{"TmdbId":126889,"Title":"Alien: Covenant","Year":2017}""")
        .Respond(TmdbApi + "keyword/180547", HttpStatusCode.OK, """{"id":180547,"name":"marvel cinematic universe (mcu)"}""")
        .Respond(TmdbApi + "discover/movie?include_adult=false&include_video=false&language=en-US&sort_by=primary_release_date.asc&with_keywords=180547&page=1", HttpStatusCode.OK,
            """{"page":1,"total_pages":2,"results":[{"id":1726,"title":"Iron Man","release_date":"2008-04-30","poster_path":"/im.jpg"},{"id":9,"title":"Untitled","release_date":""}]}""")
        .Respond(TmdbApi + "discover/movie?include_adult=false&include_video=false&language=en-US&sort_by=primary_release_date.asc&with_keywords=180547&page=2", HttpStatusCode.OK,
            """{"page":2,"total_pages":2,"results":[{"id":1724,"title":"The Incredible Hulk","release_date":"2008-06-12"}]}""");

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ACollectionIsReadWithItsPartsInReleaseOrder()
    {
        CollectionInfo collection = await new MovieMetadataClient(new HttpClient(_http), Options.Create(new ModularrOptions()))
            .GetCollectionAsync(135416, TestContext.Current.CancellationToken);

        Assert.Equal((135416, "Prometheus Collection"), (collection.Id, collection.Name));
        Assert.Equal("https://image.tmdb.org/t/p/original/ormwCxyrBJGQmuMfE6diUnZV12R.jpg", collection.Poster);
        Assert.Equal([70981, 126889], collection.Parts.Select(p => p.TmdbId));
        // The first day out anywhere: Prometheus was in cinemas before its digital release.
        Assert.Equal(new DateOnly(2012, 5, 30), collection.Parts[0].Release);
        Assert.Equal("https://image.tmdb.org/t/p/original/qsYQflQhOuhDpQ0W2aOcwqgDAeI.jpg", collection.Parts[0].Poster);
    }

    [Fact]
    public async Task AKeywordFranchiseReadsEveryDiscoverPageAndIsStoredNegated()
    {
        CollectionInfo mcu = await Tmdb(_http).GetKeywordFranchiseAsync(180547, TestContext.Current.CancellationToken);

        Assert.Equal((-180547, "Marvel Cinematic Universe (Mcu)"), (mcu.Id, mcu.Name));
        Assert.Equal([1726, 1724, 9], mcu.Parts.Select(p => p.TmdbId));
        Assert.Equal((2008, new DateOnly(2008, 4, 30)), (mcu.Parts[0].Year, mcu.Parts[0].Release));
        Assert.Null(mcu.Parts[2].Release);
        Assert.Equal("https://image.tmdb.org/t/p/w500/im.jpg", mcu.Poster);
    }

    [Fact]
    public void MissingPartsAreTheReleasedOnesNotInTheLibrary()
    {
        DateOnly today = new(2026, 10, 23);
        CollectionPart[] parts =
        [
            new(1, "Owned", 2010, new DateOnly(2010, 1, 1), null, null),
            new(2, "Released", 2012, new DateOnly(2012, 1, 1), null, null),
            new(3, "Out today", 2026, today, null, null),
            new(4, "Upcoming", 2027, new DateOnly(2027, 5, 1), null, null),
            new(5, "Undated", 0, null, null, null),
        ];
        HashSet<int> library = [1];

        Assert.Equal([2, 3], CollectionService.Missing(parts, library, today, includeUpcoming: false).Select(p => p.TmdbId));
        Assert.Equal([2, 3, 4, 5], CollectionService.Missing(parts, library, today, includeUpcoming: true).Select(p => p.TmdbId));
    }

    [Fact]
    public async Task FollowingWithAddMissingAddsOnlyThePartsNotInTheLibraryWithTheCollectionProfile()
    {
        using ModularrDbContext db = _database.NewContext();
        CancellationToken ct = TestContext.Current.CancellationToken;
        CollectionService collections = Service(db, out LibraryService library);
        await library.AddMovieAsync(70981, qualityProfileId: null, version: null, search: false, ct);

        CollectionDetail detail = await collections.FollowAsync(135416, monitored: true, qualityProfileId: 2, addMissing: true, ct);

        Assert.True(detail.Followed);
        Assert.All(detail.Parts, p => Assert.NotNull(p.MovieId));
        Movie covenant = db.Movies.Single(m => m.TmdbId == 126889);
        Assert.Equal(2, covenant.QualityProfileId);
        Assert.Equal(4, db.Movies.Single(m => m.TmdbId == 70981).QualityProfileId);
        Assert.Empty(await collections.AddMissingAsync(135416, includeUpcoming: true, fresh: false, ct));
    }

    [Fact]
    public async Task UnfollowingKeepsTheMovies()
    {
        using ModularrDbContext db = _database.NewContext();
        CancellationToken ct = TestContext.Current.CancellationToken;
        CollectionService collections = Service(db, out _);
        await collections.FollowAsync(135416, monitored: false, qualityProfileId: null, addMissing: true, ct);

        await collections.UnfollowAsync(135416, ct);

        Assert.Empty(db.Collections);
        Assert.Equal(2, db.Movies.Count());
        Assert.False((await collections.GetAsync(135416, ct)).Followed);
    }

    private static TmdbClient Tmdb(StubHttpHandler http) =>
        new(new HttpClient(http), new FixedOptionsMonitor(new ModularrOptions { Metadata = new MetadataOptions { TmdbApiKey = "a.b.c" } }));

    private CollectionService Service(ModularrDbContext db, out LibraryService library)
    {
        IOptions<ModularrOptions> options = Options.Create(new ModularrOptions());
        MovieMetadataClient movies = new(new HttpClient(_http), options);
        ActivityLog activities = new(db, new ActivityFeed());
        library = new LibraryService(
            db,
            new SeriesMetadataClient(new HttpClient(_http), options),
            movies,
            new FixedOptionsMonitor(new ModularrOptions
            {
                Paths = new PathOptions { Series = "/tv", Movies = "/movies", Anime = "/anime" },
                Media = new MediaOptions { Movies = new MediaDefaults { ProfileId = 4 } },
            }),
            new SearchRunner(null!, NullLogger<SearchRunner>.Instance),
            activities);
        return new CollectionService(db, movies, Tmdb(_http), library, activities, new MemoryCache(new MemoryCacheOptions()));
    }
}
