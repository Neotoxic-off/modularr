using System.Net;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Languages;
using Modularr.Core.Metadata;

namespace Modularr.Core.Tests.Metadata;

// Radarr SkyHookProxyFixture / SkyHookProxySearchFixture against recorded RadarrAPI responses (live variants in MetadataLiveTests).
public sealed class MovieMetadataClientTests
{
    private const string RadarrApi = "https://api.radarr.video/v1/";

    private readonly StubHttpHandler _http = new StubHttpHandler()
        .Fixture(RadarrApi + "movie/70981", "radarr-movie-70981.json")
        .Fixture(RadarrApi + "movie/imdb/tt1446714", "radarr-movie-imdb-tt1446714.json")
        .Respond(RadarrApi + "movie/imdb/", HttpStatusCode.NotFound, "[]")
        .Fixture(RadarrApi + "search?q=prometheus&year=", "radarr-search-prometheus.json")
        .Respond(RadarrApi + "search?", HttpStatusCode.OK, "[]");

    private MovieMetadataClient Client() => new(new HttpClient(_http), Options.Create(new ModularrOptions()));

    [Fact]
    public async Task ShouldBeAbleToGetMovieDetail()
    {
        MovieInfo movie = await Client().GetMovieAsync(70981, TestContext.Current.CancellationToken);

        ValidateMovie(movie);
        Assert.Equal("Prometheus", movie.Title);
        Assert.Equal("tt1446714", movie.ImdbId);
        Assert.Equal(Language.English, movie.OriginalLanguage);
        Assert.Equal(2012, movie.Year);
        Assert.Null(movie.SecondaryYear);
        Assert.Equal(new DateTime(2012, 5, 30, 0, 0, 0, DateTimeKind.Utc), movie.InCinemas);
        Assert.Equal(MovieStatus.Released, movie.Status);
        Assert.Equal(124, movie.Runtime);
        Assert.Equal("R", movie.Certification);
        Assert.Equal("Dune Entertainment", movie.Studio);
        Assert.Equal("cJMIzdf1eo8", movie.YouTubeTrailerId);
        Assert.Equal(135416, movie.CollectionTmdbId);
        Assert.Equal("Prometheus Collection", movie.CollectionTitle);
        Assert.Equal(new MetadataRating(708865, 7m), movie.Ratings.Imdb);
        Assert.Equal(new MetadataRating(13738, 6.614m), movie.Ratings.Tmdb);
        Assert.Contains(348, movie.RecommendationTmdbIds);
        Assert.Contains("Prometeo", movie.AlternativeTitles);
        Assert.Contains(movie.Translations, t => t.Language == Language.English && t.Title == "Prometheus");
        Assert.Contains(new MetadataImage(MediaCoverType.Poster, "https://image.tmdb.org/t/p/original/qsYQflQhOuhDpQ0W2aOcwqgDAeI.jpg"), movie.Images);
    }

    [Fact]
    public async Task GettingDetailsOfInvalidMovie()
    {
        MetadataNotFoundException ex = await Assert.ThrowsAsync<MetadataNotFoundException>(() => Client().GetMovieAsync(1, TestContext.Current.CancellationToken));

        Assert.Equal("1", ex.Id);
    }

    [Theory]
    [InlineData("tt1446714")]
    [InlineData("1446714")]
    public async Task GetMovieByImdbNormalizesTheId(string imdbId)
    {
        MovieInfo movie = await Client().GetMovieByImdbAsync(imdbId, TestContext.Current.CancellationToken);

        Assert.Equal(70981, movie.TmdbId);
        Assert.Equal(RadarrApi + "movie/imdb/tt1446714", _http.Requests.Single());
    }

    [Theory]
    [InlineData("4565se")]
    [InlineData("tt9805708")]
    public async Task GetMovieByImdbNotFound(string imdbId)
    {
        await Assert.ThrowsAsync<MetadataNotFoundException>(() => Client().GetMovieByImdbAsync(imdbId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ServerErrorsAreSourceFailures()
    {
        StubHttpHandler http = new StubHttpHandler().Respond(RadarrApi, HttpStatusCode.BadGateway);
        MovieMetadataClient client = new(new HttpClient(http), Options.Create(new ModularrOptions()));

        await Assert.ThrowsAsync<MetadataSourceException>(() => client.SearchMoviesAsync("Prometheus", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<MetadataSourceException>(() => client.GetMovieAsync(70981, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SuccessfulSearchByTitle()
    {
        IReadOnlyList<MovieInfo> result = await Client().SearchMoviesAsync("Prometheus", TestContext.Current.CancellationToken);

        Assert.NotEmpty(result);
        Assert.Equal("Prometheus", result[0].Title);
    }

    // Upstream cases use other movies (Star Wars: The Last Jedi, Annihilation, Citizen Kane, A Trip to the Moon); the recorded one is Prometheus.
    [Theory]
    [InlineData("imdb:tt1446714")]
    [InlineData("imdbid: tt1446714")]
    [InlineData("https://www.imdb.com/title/tt1446714/")]
    [InlineData("https://www.themoviedb.org/movie/70981-prometheus")]
    [InlineData("tmdb:70981")]
    [InlineData("TMDBID: 70981")]
    public async Task SuccessfulSearchById(string term)
    {
        IReadOnlyList<MovieInfo> result = await Client().SearchMoviesAsync(term, TestContext.Current.CancellationToken);

        Assert.Equal("Prometheus", Assert.Single(result).Title);
        Assert.DoesNotContain(_http.Requests, r => r.Contains("/search?", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Prometheus 2012", "search?q=prometheus&year=2012")]
    [InlineData("Prometheus (2012)", "search?q=prometheus&year=2012")]
    [InlineData("Matrix, The", "search?q=matrix&year=")]
    [InlineData("the_lord of the rings", "search?q=the%2Blord%2Bof%2Bthe%2Brings&year=")]
    public async Task BuildsTheSearchQuery(string term, string expected)
    {
        await Client().SearchMoviesAsync(term, TestContext.Current.CancellationToken);

        Assert.Equal(RadarrApi + expected, _http.Requests.Single());
    }

    [Theory]
    [InlineData("tmdbid:")]
    [InlineData("tmdbid: 99999999999999999999")]
    [InlineData("tmdbid: 0")]
    [InlineData("tmdbid: -12")]
    [InlineData("tmdbid:1")]
    [InlineData("adjalkwdjkalwdjklawjdlKAJD;EF")]
    [InlineData("imdb: tt9805708")]
    [InlineData("https://www.UNKNOWN-DOMAIN.com/title/tt0033467/")]
    [InlineData("https://www.themoviedb.org/MALFORMED/775-le-voyage-dans-la-lune")]
    public async Task NoSearchResult(string term)
    {
        Assert.Empty(await Client().SearchMoviesAsync(term, TestContext.Current.CancellationToken));
    }

    internal static void ValidateMovie(MovieInfo movie)
    {
        Assert.False(string.IsNullOrWhiteSpace(movie.Title));
        Assert.False(string.IsNullOrWhiteSpace(movie.Overview));
        Assert.NotNull(movie.InCinemas);
        Assert.NotEmpty(movie.Images);
        Assert.False(string.IsNullOrWhiteSpace(movie.ImdbId));
        Assert.False(string.IsNullOrWhiteSpace(movie.Studio));
        Assert.True(movie.Runtime > 0);
        Assert.True(movie.TmdbId > 0);
    }
}
