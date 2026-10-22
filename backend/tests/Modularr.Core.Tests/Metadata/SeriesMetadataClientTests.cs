using System.Net;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Languages;
using Modularr.Core.Metadata;

namespace Modularr.Core.Tests.Metadata;

// Sonarr SkyHookProxyFixture / SkyHookProxySearchFixture against recorded SkyHook responses (live variants in MetadataLiveTests).
public sealed class SeriesMetadataClientTests
{
    private const string SkyHook = "https://skyhook.sonarr.tv/v2/tvdb/";
    private const string DailySeries = "https://services.sonarr.tv/v1/dailyseries";

    private readonly StubHttpHandler _http = new StubHttpHandler()
        .Fixture(SkyHook + "shows/eng/76885/official", "skyhook-show-76885.json")
        .Fixture(SkyHook + "shows/eng/79274/official", "skyhook-show-79274.json")
        .Fixture(SkyHook + "search/?language=eng&term=the%20simpsons", "skyhook-search-the-simpsons.json")
        .Respond(SkyHook + "search/", HttpStatusCode.OK, "[]")
        .Fixture(DailySeries, "sonarr-dailyseries.json");

    private SeriesMetadataClient Client() => new(new HttpClient(_http), Options.Create(new ModularrOptions()));

    [Fact]
    public async Task GetSeriesMapsAnimeWithAbsoluteNumbering()
    {
        SeriesInfo series = await Client().GetSeriesAsync(76885, cancellationToken: TestContext.Current.CancellationToken);

        ValidateSeries(series);
        ValidateEpisodes(series.Episodes);
        Assert.Equal("Cowboy Bebop", series.Title);
        Assert.Equal("カウボーイビバップ", series.OriginalTitle);
        Assert.Equal(Language.Japanese, series.OriginalLanguage);
        Assert.Equal([1, 5], series.MalIds);
        Assert.Equal([1, 5], series.AniListIds);
        Assert.Contains("Anime", series.Genres);
        Assert.Equal(SeriesStatus.Ended, series.Status);
        Assert.Equal(new DateOnly(1998, 4, 3), series.FirstAired);
        Assert.Equal(1998, series.Year);
        Assert.Equal(new TimeOnly(17, 0), series.AirTime);
        Assert.Equal("TV-MA", series.Certification);
        Assert.False(series.IsDaily);
        Assert.Equal(["official", "dvd"], series.SeasonTypes.Select(t => t.Type));
        Assert.Equal([0, 1], series.Seasons.Select(s => s.SeasonNumber));
        Assert.Contains(series.Translations, t => t.Language == Language.English && t.Title == "Cowboy Bebop");

        // Aired order and absolute order diverge: the first broadcast slot was session #2.
        EpisodeInfo firstAired = series.Episodes.Single(e => e.SeasonNumber == 1 && e.EpisodeNumber == 1);
        Assert.Equal(2, firstAired.AbsoluteEpisodeNumber);
        Assert.Equal(1, series.Episodes.Single(e => e.SeasonNumber == 1 && e.EpisodeNumber == 13).AbsoluteEpisodeNumber);
        Assert.Equal(new DateOnly(1998, 4, 3), firstAired.AirDate);
        Assert.Equal(26, series.Episodes.Count(e => e.AbsoluteEpisodeNumber.HasValue));
        Assert.All(series.Episodes.Where(e => e.SeasonNumber == 0), e => Assert.Null(e.AbsoluteEpisodeNumber));

        EpisodeInfo finale = series.Episodes.Single(e => e.AbsoluteEpisodeNumber == 26);
        Assert.Equal("Session #26: The Real Folk Blues (2)", finale.Title);
        Assert.Equal("series", finale.FinaleType);
        Assert.Equal(new DateTime(1999, 4, 23, 8, 0, 0, DateTimeKind.Utc), finale.AirDateUtc);
        Assert.Equal([new MetadataImage(MediaCoverType.Screenshot, "https://artworks.thetvdb.com/banners/episodes/76885/300648.jpg")], finale.Images);
    }

    [Fact]
    public async Task GetSeriesFlagsDailyShows()
    {
        SeriesInfo series = await Client().GetSeriesAsync(79274, cancellationToken: TestContext.Current.CancellationToken);

        ValidateSeries(series);
        ValidateEpisodes(series.Episodes);
        Assert.Equal("The Colbert Report", series.Title);
        Assert.True(series.IsDaily);
        Assert.Equal(1459, series.Episodes.Count);

        EpisodeInfo episode = series.Episodes.Single(e => e.SeasonNumber == 2 && e.EpisodeNumber == 57);
        Assert.Equal("Gov. Mike Huckabee", episode.Title);
        Assert.Equal(new DateOnly(2006, 5, 2), episode.AirDate);
        Assert.Equal(new DateTime(2006, 5, 3, 3, 30, 0, DateTimeKind.Utc), episode.AirDateUtc);
    }

    [Fact]
    public async Task GetSeriesKeepsWorkingWhenTheDailyListIsUnreachable()
    {
        StubHttpHandler http = new StubHttpHandler()
            .Fixture(SkyHook + "shows/eng/79274/official", "skyhook-show-79274.json")
            .Respond(DailySeries, HttpStatusCode.InternalServerError);

        SeriesInfo series = await new SeriesMetadataClient(new HttpClient(http), Options.Create(new ModularrOptions()))
            .GetSeriesAsync(79274, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(series.IsDaily);
    }

    [Fact]
    public async Task GettingDetailsOfInvalidSeries()
    {
        MetadataNotFoundException ex = await Assert.ThrowsAsync<MetadataNotFoundException>(
            () => Client().GetSeriesAsync(int.MaxValue, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("2147483647", ex.Id);
    }

    [Fact]
    public async Task GetSeriesUsesTheRequestedLanguageAndSeasonType()
    {
        await Assert.ThrowsAsync<MetadataNotFoundException>(
            () => Client().GetSeriesAsync(76885, Language.French, "absolute", TestContext.Current.CancellationToken));

        Assert.Equal(SkyHook + "shows/fra/76885/absolute", _http.Requests.Single());
        // SkyHook rejects requests without a User-Agent with a 400.
        Assert.StartsWith("Modularr/", _http.UserAgents.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerErrorsAreSourceFailures()
    {
        StubHttpHandler http = new StubHttpHandler().Respond(SkyHook, HttpStatusCode.ServiceUnavailable);
        SeriesMetadataClient client = new(new HttpClient(http), Options.Create(new ModularrOptions()));

        await Assert.ThrowsAsync<MetadataSourceException>(() => client.SearchSeriesAsync("House", cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<MetadataSourceException>(() => client.GetSeriesAsync(1, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UsesConfiguredBaseUrls()
    {
        StubHttpHandler http = new StubHttpHandler().Respond("http://mirror.local/tvdb/search/", HttpStatusCode.OK, "[]");
        ModularrOptions options = new() { Metadata = new MetadataOptions { SkyHookUrl = "http://mirror.local/tvdb", SonarrServicesUrl = "http://mirror.local/services" } };

        await new SeriesMetadataClient(new HttpClient(http), Options.Create(options)).SearchSeriesAsync("House", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["http://mirror.local/tvdb/search/?language=eng&term=house", "http://mirror.local/services/dailyseries"], http.Requests);
    }

    [Fact]
    public async Task SuccessfulSearch()
    {
        IReadOnlyList<SeriesInfo> result = await Client().SearchSeriesAsync("The Simpsons", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(result);
        Assert.Equal("The Simpsons", result[0].Title);
        Assert.All(result, s => Assert.Empty(s.Episodes));
    }

    // Upstream cases use Doctor Who (78804); the recorded show is Cowboy Bebop.
    [Theory]
    [InlineData("tvdb:76885")]
    [InlineData("tvdbid:76885")]
    [InlineData("tvdbid: 76885 ")]
    [InlineData("TVDB:76885")]
    public async Task SuccessfulSearchByTvdbId(string term)
    {
        IReadOnlyList<SeriesInfo> result = await Client().SearchSeriesAsync(term, cancellationToken: TestContext.Current.CancellationToken);

        SeriesInfo series = Assert.Single(result);
        Assert.Equal("Cowboy Bebop", series.Title);
        Assert.Empty(series.Episodes);
    }

    [Theory]
    [InlineData("tvdbid:")]
    [InlineData("tvdbid: 99999999999999999999")]
    [InlineData("tvdbid: 0")]
    [InlineData("tvdbid: -12")]
    public async Task NoSearchResultWithoutAskingSkyHook(string term)
    {
        Assert.Empty(await Client().SearchSeriesAsync(term, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(_http.Requests);
    }

    [Theory]
    [InlineData("tvdbid:289578")]
    [InlineData("adjalkwdjkalwdjklawjdlKAJD")]
    public async Task NoSearchResult(string term)
    {
        Assert.Empty(await Client().SearchSeriesAsync(term, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PassesOtherIdTermsToSkyHook()
    {
        await Client().SearchSeriesAsync("imdb:tt0496424", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(SkyHook + "search/?language=eng&term=imdb%3Att0496424", _http.Requests[0]);
    }

    [Theory]
    [InlineData("/tv/The Simpsons")]
    [InlineData(@"C:\tv\The Simpsons")]
    [InlineData(@"\\server\tv")]
    public async Task RejectsPathsAsSearchTerms(string term)
    {
        await Assert.ThrowsAsync<InvalidSearchTermException>(() => Client().SearchSeriesAsync(term, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(_http.Requests);
    }

    internal static void ValidateSeries(SeriesInfo series)
    {
        Assert.False(string.IsNullOrWhiteSpace(series.Title));
        Assert.False(string.IsNullOrWhiteSpace(series.Overview));
        Assert.NotNull(series.AirTime);
        Assert.NotNull(series.FirstAired);
        Assert.NotEmpty(series.Images);
        Assert.False(string.IsNullOrWhiteSpace(series.ImdbId));
        Assert.False(string.IsNullOrWhiteSpace(series.Network));
        Assert.True(series.Runtime > 0);
        Assert.False(string.IsNullOrWhiteSpace(series.TitleSlug));
        Assert.True(series.TvdbId > 0);
    }

    internal static void ValidateEpisodes(IReadOnlyList<EpisodeInfo> episodes)
    {
        Assert.NotEmpty(episodes);
        Assert.All(episodes.GroupBy(e => (e.SeasonNumber, e.EpisodeNumber)), g => Assert.Single(g));
        Assert.Contains(episodes, e => e.SeasonNumber > 0);
        Assert.Contains(episodes, e => !string.IsNullOrWhiteSpace(e.Overview));
        Assert.Contains(episodes, e => !string.IsNullOrWhiteSpace(e.Title));

        Assert.All(episodes, e =>
        {
            Assert.NotEqual(0, e.EpisodeNumber + e.SeasonNumber);

            if (e.AirDateUtc.HasValue)
            {
                Assert.Equal(DateTimeKind.Utc, e.AirDateUtc.Value.Kind);
            }

            Assert.DoesNotContain(e.Images, i => i.CoverType == MediaCoverType.Screenshot && i.Url.Contains("-940.", StringComparison.Ordinal));
        });
    }
}
