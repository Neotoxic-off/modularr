using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Modularr.Core.Configuration;
using Modularr.Core.Languages;
using Modularr.Core.Subtitles;
using Modularr.Core.Tests.Library;
using Modularr.Core.Tests.Metadata;

namespace Modularr.Core.Tests.Subtitles;

// The fixtures follow the documented OpenSubtitles REST v1 shapes (no account was used to record them).
public sealed class OpenSubtitlesProviderTests : IDisposable
{
    private const string Api = "https://api.opensubtitles.com/api/v1/";

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10 });

    public void Dispose() => _cache.Dispose();

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Subtitles", "Fixtures", name));

    private OpenSubtitlesProvider Provider(StubHttpHandler http, OpenSubtitlesOptions? account = null) =>
        new(new HttpClient(http), new FixedOptionsMonitor(new ModularrOptions { Subtitles = new SubtitleOptions { OpenSubtitles = account ?? new OpenSubtitlesOptions { ApiKey = "key" } } }), _cache);

    private static SubtitleQuery Movie(params Language[] languages) =>
        new() { Title = "Inception", Year = 2010, ImdbId = "tt1375666", TmdbId = 27205, MovieHash = "8e245d9679d31e12", Languages = languages };

    [Fact]
    public async Task AMovieIsSearchedByImdbIdAndHashWithSortedLanguages()
    {
        StubHttpHandler http = new StubHttpHandler().Respond(Api + "subtitles?", HttpStatusCode.OK, Fixture("opensubtitles-search-movie.json"));

        IReadOnlyList<SubtitleCandidate> found = await Provider(http).SearchAsync(Movie(Language.French, Language.English), TestContext.Current.CancellationToken);

        Assert.Equal(Api + "subtitles?imdb_id=1375666&languages=en,fr&moviehash=8e245d9679d31e12", Assert.Single(http.Requests));
        Assert.Equal("Modularr v1.0", http.UserAgents[0]);
        // German is not asked for, the entry without a file cannot be downloaded.
        Assert.Equal(
            [
                new SubtitleCandidate("OpenSubtitles", "4113460", Language.French, "Inception.2010.1080p.BluRay.x264-SPARKS", false, false, true, 21854),
                new SubtitleCandidate("OpenSubtitles", "4113555", Language.English, "Inception.2010.720p.BluRay.x264-REWARD", true, false, false, 98000),
                new SubtitleCandidate("OpenSubtitles", "4113600", Language.French, "Inception.2010.FORCED", false, true, false, 500),
            ],
            found);
    }

    [Fact]
    public async Task AMovieWithoutImdbIdIsSearchedByTmdbId()
    {
        StubHttpHandler http = new StubHttpHandler().Respond(Api + "subtitles?", HttpStatusCode.OK, """{ "data": [] }""");

        await Provider(http).SearchAsync(Movie(Language.French) with { ImdbId = null, MovieHash = null }, TestContext.Current.CancellationToken);

        Assert.Equal(Api + "subtitles?languages=fr&tmdb_id=27205", Assert.Single(http.Requests));
    }

    [Fact]
    public async Task AnEpisodeIsSearchedByTitleAndKeepsOnlyThatSeries()
    {
        StubHttpHandler http = new StubHttpHandler().Respond(Api + "subtitles?", HttpStatusCode.OK, Fixture("opensubtitles-search-episode.json"));
        SubtitleQuery episode = new() { Title = "The Office (US)", Season = 1, Episode = 1, Languages = [Language.French, Language.PortugueseBrazil] };

        IReadOnlyList<SubtitleCandidate> found = await Provider(http).SearchAsync(episode, TestContext.Current.CancellationToken);

        Assert.Equal(Api + "subtitles?episode_number=1&languages=fr,pt-br&query=The%20Office%20%28US%29&season_number=1&type=episode", Assert.Single(http.Requests));
        Assert.Equal("8800001", Assert.Single(found).Id);
    }

    [Fact]
    public async Task ADownloadFollowsTheLinkOnAnOpenSubtitlesHost()
    {
        StubHttpHandler http = new StubHttpHandler()
            .Respond(Api + "download", HttpStatusCode.OK, Fixture("opensubtitles-download.json"))
            .Respond("https://www.opensubtitles.com/download/", HttpStatusCode.OK, "1\n00:00:01,000 --> 00:00:02,000\nBonjour\n");

        SubtitleDownload download = await Provider(http).DownloadAsync("4113460", TestContext.Current.CancellationToken);

        Assert.Equal("Inception.2010.1080p.BluRay.x264-SPARKS.srt", download.FileName);
        Assert.EndsWith("Bonjour\n", System.Text.Encoding.UTF8.GetString(download.Content), StringComparison.Ordinal);
        Assert.Equal("""{"file_id":4113460}""", http.Bodies[0]);
    }

    [Theory]
    [InlineData("http://www.opensubtitles.com/download/x.srt")]
    [InlineData("https://evil.example/download/x.srt")]
    [InlineData("https://opensubtitles.com.evil.example/x.srt")]
    public async Task ADownloadLinkOffTheProviderIsRefused(string link)
    {
        StubHttpHandler http = new StubHttpHandler().Respond(Api + "download", HttpStatusCode.OK, $$"""{ "link": "{{link}}", "file_name": "x.srt" }""");

        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(http).DownloadAsync("1", TestContext.Current.CancellationToken));

        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task AnAccountLogsInOnceAndSendsItsToken()
    {
        StubHttpHandler http = new StubHttpHandler()
            .Respond(Api + "login", HttpStatusCode.OK, """{ "token": "abc", "base_url": "api.opensubtitles.com", "status": 200 }""")
            .Respond(Api + "subtitles?", HttpStatusCode.OK, """{ "data": [] }""");
        OpenSubtitlesProvider provider = Provider(http, new OpenSubtitlesOptions { ApiKey = "key", Username = "me", Password = "secret" });

        await provider.SearchAsync(Movie(Language.French), TestContext.Current.CancellationToken);
        await provider.SearchAsync(Movie(Language.French), TestContext.Current.CancellationToken);

        Assert.Equal(1, http.Requests.Count(r => r.EndsWith("/login", StringComparison.Ordinal)));
        Assert.Equal("""{"username":"me","password":"secret"}""", http.Bodies[0]);
    }

    [Fact]
    public void WithoutAnApiKeyTheProviderIsOff() =>
        Assert.False(Provider(new StubHttpHandler(), new OpenSubtitlesOptions { Username = "me" }).Enabled);

    [Fact]
    public async Task AFileIdMustBeANumber() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Provider(new StubHttpHandler()).DownloadAsync("../1", TestContext.Current.CancellationToken));
}
