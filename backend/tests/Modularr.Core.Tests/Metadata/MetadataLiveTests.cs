using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Metadata;

namespace Modularr.Core.Tests.Metadata;

// Upstream [IntegrationTest] cases verbatim, against the real SkyHook / RadarrAPI. Opt in with MODULARR_LIVE_METADATA=1.
public sealed class MetadataLiveTests
{
    private static SeriesMetadataClient SeriesClient()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("MODULARR_LIVE_METADATA") == "1", "Set MODULARR_LIVE_METADATA=1 to query the live metadata sources.");
        return new SeriesMetadataClient(new HttpClient(), Options.Create(new ModularrOptions()));
    }

    private static MovieMetadataClient MovieClient()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("MODULARR_LIVE_METADATA") == "1", "Set MODULARR_LIVE_METADATA=1 to query the live metadata sources.");
        return new MovieMetadataClient(new HttpClient(), Options.Create(new ModularrOptions()));
    }

    [Theory]
    [InlineData(75978, "Family Guy")]
    [InlineData(83462, "Castle (2009)")]
    [InlineData(266189, "The Blacklist")]
    public async Task ShouldBeAbleToGetSeriesDetail(int tvdbId, string title)
    {
        SeriesInfo series = await SeriesClient().GetSeriesAsync(tvdbId, cancellationToken: TestContext.Current.CancellationToken);

        SeriesMetadataClientTests.ValidateSeries(series);
        SeriesMetadataClientTests.ValidateEpisodes(series.Episodes);
        Assert.Equal(title, series.Title);
    }

    [Fact]
    public async Task GettingDetailsOfInvalidSeries()
    {
        SeriesMetadataClient client = SeriesClient();
        await Assert.ThrowsAsync<MetadataNotFoundException>(() => client.GetSeriesAsync(int.MaxValue, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldNotHavePeriodAtStartOfTitleSlug()
    {
        SeriesInfo series = await SeriesClient().GetSeriesAsync(79099, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(series.TitleSlug!.StartsWith('.'));
    }

    [Theory]
    [InlineData("The Simpsons", "The Simpsons")]
    [InlineData("South Park", "South Park")]
    [InlineData("Franklin & Bash", "Franklin & Bash")]
    [InlineData("House", "House")]
    [InlineData("Mr. D", "Mr. D")]
    [InlineData("Rob & Big", "Rob & Big")]
    [InlineData("M*A*S*H", "M*A*S*H")]
    [InlineData("tvdb:78804", "Doctor Who (2005)")]
    [InlineData("tvdbid:78804", "Doctor Who (2005)")]
    [InlineData("tvdbid: 78804 ", "Doctor Who (2005)")]
    [InlineData("imdb:tt0496424", "30 Rock")]
    public async Task SuccessfulSeriesSearch(string term, string expected)
    {
        IReadOnlyList<SeriesInfo> result = await SeriesClient().SearchSeriesAsync(term, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(result);
        Assert.Equal(expected, result[0].Title);
    }

    [Theory]
    [InlineData("tvdbid:289578")]
    [InlineData("adjalkwdjkalwdjklawjdlKAJD")]
    public async Task NoSeriesSearchResult(string term)
    {
        Assert.Empty(await SeriesClient().SearchSeriesAsync(term, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(11, "Star Wars")]
    [InlineData(2, "Ariel")]
    [InlineData(70981, "Prometheus")]
    [InlineData(238, "The Godfather")]
    public async Task ShouldBeAbleToGetMovieDetail(int tmdbId, string title)
    {
        MovieInfo movie = await MovieClient().GetMovieAsync(tmdbId, TestContext.Current.CancellationToken);

        MovieMetadataClientTests.ValidateMovie(movie);
        Assert.Equal(title, movie.Title);
    }

    [Theory]
    [InlineData("Prometheus", "Prometheus")]
    [InlineData("imdb:tt2527336", "Star Wars: The Last Jedi")]
    [InlineData("imdb:tt2798920", "Annihilation")]
    [InlineData("https://www.imdb.com/title/tt0033467/", "Citizen Kane")]
    [InlineData("https://www.themoviedb.org/movie/775-le-voyage-dans-la-lune", "A Trip to the Moon")]
    public async Task SuccessfulMovieSearch(string term, string expected)
    {
        IReadOnlyList<MovieInfo> result = await MovieClient().SearchMoviesAsync(term, TestContext.Current.CancellationToken);

        Assert.NotEmpty(result);
        Assert.Equal(expected, result[0].Title);
    }

    [Theory]
    [InlineData("tmdbid:1")]
    [InlineData("adjalkwdjkalwdjklawjdlKAJD;EF")]
    [InlineData("imdb: tt9805708")]
    [InlineData("https://www.UNKNOWN-DOMAIN.com/title/tt0033467/")]
    [InlineData("https://www.themoviedb.org/MALFORMED/775-le-voyage-dans-la-lune")]
    public async Task NoMovieSearchResult(string term)
    {
        Assert.Empty(await MovieClient().SearchMoviesAsync(term, TestContext.Current.CancellationToken));
    }
}
