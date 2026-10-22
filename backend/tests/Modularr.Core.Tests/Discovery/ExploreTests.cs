using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Discovery;
using Modularr.Core.Languages;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;
using Modularr.Core.Tests.Metadata;
using Modularr.Core.Tests.Persistence;

namespace Modularr.Core.Tests.Discovery;

// Recorded-shape TVmaze, AniList, TMDB and RadarrAPI answers; nothing goes to the network.
public sealed class ExploreTests : IDisposable
{
    private const string TvMaze = "https://api.tvmaze.com/";
    private const string AniList = "https://graphql.anilist.co/";
    private const string Tmdb = "https://api.themoviedb.org/3/";
    private const string RadarrApi = "https://api.radarr.video/v1/";

    private readonly TestDatabase _database = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 1000 });

    public void Dispose()
    {
        _cache.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task SeriesComeFromTheTvMazeIndexFilteredSortedAndPaged()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Series owned = _database.AddSeries(248742);
        MediaFile file = _database.Insert(new MediaFile { SeriesId = owned.Id, Path = "/tv/248742/e.mkv", Quality = new QualityModel(Quality.Hdtv720p, new Revision()) })[0];
        _database.Insert(new Episode { SeriesId = owned.Id, SeasonNumber = 1, EpisodeNumber = 1, MediaFileId = file.Id });
        StubHttpHandler http = new StubHttpHandler().Fixture(TvMaze + "shows?page=0", "tvmaze-shows-page0.json");
        ShowDiscoveryClient shows = new(new HttpClient(http), Options.Create(new ModularrOptions()));
        ShowIndex index = new(shows.GetShowIndexPageAsync, TimeSpan.Zero, CancellationToken.None);
        DiscoveryService discovery = DiscoveryServiceTests.Create(new HttpClient(http), _cache, _database.NewContext(), index: index);

        ExplorePage first = await discovery.ExploreAsync(MediaType.Series, new ExploreQuery(), ct);
        await index.Loading;
        ExplorePage popular = await discovery.ExploreAsync(MediaType.Series, new ExploreQuery(), ct);

        Assert.True(first.Loading || first.Items.Count > 0);
        Assert.False(popular.Loading);

        // News, anime and shows without a TVDB id are left out; the most followed come first.
        Assert.Equal([264492, 248742, 327417, 269550, 450001], popular.Items.Select(i => i.TvdbId!.Value));
        DiscoveryItem person = popular.Items[1];
        Assert.Equal((owned.Id, true, "English"), (person.LibraryId, person.HasFile, person.Language));
        Assert.False(popular.HasMore);
        Assert.Equal(TvMaze + "shows?page=1", http.Requests[^1]);

        ExplorePage drama = await discovery.ExploreAsync(
            MediaType.Series, new ExploreQuery { Genre = "drama", YearFrom = 2014, Status = ExploreStatus.Ended, Sort = ExploreSort.Rating }, ct);
        Assert.Equal(["La casa de papel", "Bitten"], drama.Items.Select(i => i.Title));

        ExplorePage spanish = await discovery.ExploreAsync(MediaType.Series, new ExploreQuery { Language = Language.Spanish, Sort = ExploreSort.Date }, ct);
        Assert.Equal([327417], spanish.Items.Select(i => i.TvdbId!.Value));

        ExplorePage byTitle = await discovery.ExploreAsync(MediaType.Series, new ExploreQuery { Sort = ExploreSort.Title, MinRating = 7 }, ct);
        Assert.Equal(["Bitten", "La casa de papel", "Person of Interest"], byTitle.Items.Select(i => i.Title));

        Assert.Empty((await discovery.ExploreAsync(MediaType.Series, new ExploreQuery { Page = 2 }, ct)).Items);
        Assert.Equal(["Action", "Crime", "Drama", "Horror", "Romance", "Science-Fiction", "Thriller"], await discovery.ExploreGenresAsync(MediaType.Series, ct));
    }

    [Fact]
    public async Task AnimeFiltersGoToAniList()
    {
        StubHttpHandler http = new StubHttpHandler()
            .Fixture(AniList, "anilist-explore.json")
            .Fixture("https://raw.githubusercontent.com/Fribb/anime-lists/", "anime-mapping.json");

        ExplorePage page = await DiscoveryServiceTests.Create(new HttpClient(http), _cache, _database.NewContext()).ExploreAsync(
            MediaType.Anime,
            new ExploreQuery
            {
                Page = 2, Genre = "Action", YearFrom = 2020, YearTo = 2025, MinRating = 7, Status = ExploreStatus.Continuing,
                Language = Language.Japanese, Sort = ExploreSort.Rating,
            },
            TestContext.Current.CancellationToken);

        // The unmapped show has no TVDB id to add it by.
        DiscoveryItem knight = Assert.Single(page.Items);
        Assert.Equal((453028, "The Exiled Heavy Knight Knows How to Game the System", 7.2m, "Continuing"), (knight.TvdbId!.Value, knight.Title, knight.Rating, knight.Status));
        Assert.True(page.HasMore);
        string body = http.Bodies.Single(b => b.Contains("pageInfo", StringComparison.Ordinal));
        foreach (string expected in new[]
        {
            "\"page\":2", "\"sort\":\"SCORE_DESC\"", "\"genre\":\"Action\"", "\"from\":20199999", "\"to\":20260000", "\"score\":69",
            "\"status\":\"RELEASING\"", "\"country\":\"JP\"",
        })
        {
            Assert.Contains(expected, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnimeGenresLeaveAdultOnesOut()
    {
        StubHttpHandler http = new StubHttpHandler().Fixture(AniList, "anilist-genres.json");

        IReadOnlyList<string> genres = await DiscoveryServiceTests.Create(new HttpClient(http), _cache, _database.NewContext())
            .ExploreGenresAsync(MediaType.Anime, TestContext.Current.CancellationToken);

        Assert.Contains("Mecha", genres);
        Assert.DoesNotContain("Hentai", genres);
    }

    [Fact]
    public async Task MoviesWithoutAKeyAreTheKeylessListsFlaggedLimited()
    {
        StubHttpHandler http = new StubHttpHandler()
            .Fixture(RadarrApi + "list/tmdb/trending", "radarr-list-tmdb-trending.json")
            .Respond(RadarrApi + "list/", HttpStatusCode.OK, "[]");

        ExplorePage page = await DiscoveryServiceTests.Create(new HttpClient(http), _cache, _database.NewContext())
            .ExploreAsync(MediaType.Movie, new ExploreQuery { Sort = ExploreSort.Title }, TestContext.Current.CancellationToken);

        Assert.True(page.Limited);
        Assert.Null(page.Error);
        Assert.Equal(page.Items.Select(i => i.Title).Order(StringComparer.OrdinalIgnoreCase), page.Items.Select(i => i.Title));
        Assert.DoesNotContain(http.Requests, r => r.StartsWith(Tmdb, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MoviesWithAKeyUseTmdbDiscover()
    {
        StubHttpHandler http = new StubHttpHandler()
            .Fixture(Tmdb + "genre/movie/list", "tmdb-genres.json")
            .Fixture(Tmdb + "discover/movie", "tmdb-discover.json");
        DiscoveryService discovery = DiscoveryServiceTests.Create(new HttpClient(http), _cache, _database.NewContext(), tmdbKey: "v3key");

        ExplorePage page = await discovery.ExploreAsync(
            MediaType.Movie,
            new ExploreQuery { Genre = "Drama", YearFrom = 1990, YearTo = 1999, MinRating = 8, Language = Language.English, Status = ExploreStatus.Released, Sort = ExploreSort.Rating },
            TestContext.Current.CancellationToken);

        Assert.False(page.Limited);
        Assert.True(page.HasMore);
        DiscoveryItem shawshank = page.Items[0];
        Assert.Equal((278, 1994, 8.7m, "English"), (shawshank.TmdbId!.Value, shawshank.Year, shawshank.Rating, shawshank.Language));
        Assert.Equal(["Drama", "Crime"], shawshank.Genres);
        Assert.Equal("https://image.tmdb.org/t/p/w500/9cqNxx0GxF0bflZmeSMuL5tnGzr.jpg", shawshank.Poster);
        Assert.Equal((null, null, null), (page.Items[1].Year, page.Items[1].Rating, page.Items[1].Overview));
        string request = http.Requests.Single(r => r.Contains("discover", StringComparison.Ordinal));
        foreach (string expected in new[]
        {
            "sort_by=vote_average.desc", "vote_count.gte=200", "with_genres=18", "primary_release_date.gte=1990-01-01", "primary_release_date.lte=1999-12-31",
            "vote_average.gte=8", "with_original_language=en", "api_key=v3key",
        })
        {
            Assert.Contains(expected, request, StringComparison.Ordinal);
        }

        await Assert.ThrowsAsync<ArgumentException>(() => discovery.ExploreAsync(MediaType.Movie, new ExploreQuery { Genre = "Western" }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TmdbErrorsNeverShowTheKey()
    {
        StubHttpHandler http = new StubHttpHandler().Respond(Tmdb, HttpStatusCode.Unauthorized);

        ExplorePage page = await DiscoveryServiceTests.Create(new HttpClient(http), _cache, _database.NewContext(), tmdbKey: "secretkey")
            .ExploreAsync(MediaType.Movie, new ExploreQuery(), TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.Contains("Unauthorized", page.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("secretkey", page.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MediaType.Movie, 0, null, null, null)]
    [InlineData(MediaType.Movie, 501, null, null, null)]
    [InlineData(MediaType.Movie, 1, ExploreStatus.Ended, null, null)]
    [InlineData(MediaType.Series, 1, ExploreStatus.Released, null, null)]
    [InlineData(MediaType.Series, 1, null, 2020, 2010)]
    [InlineData(MediaType.Anime, 1, null, 1500, null)]
    public void InvalidFiltersAreRefused(MediaType kind, int page, ExploreStatus? status, int? yearFrom, int? yearTo) =>
        Assert.Throws<ArgumentException>(() => new ExploreQuery { Page = page, Status = status, YearFrom = yearFrom, YearTo = yearTo }.EnsureValid(kind));
}
