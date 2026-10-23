using System.Net;
using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Cardigann;
using Modularr.Core.Indexers.Definitions;
using Modularr.Core.Tests.Indexers.Definitions;
using static Modularr.Core.Tests.Indexers.Cardigann.CardigannFixtures;

namespace Modularr.Core.Tests.Indexers.Cardigann;

// Site and definition problems come back as typed failures; only caller cancellation throws.
public sealed class CardigannFailureTests
{
    private static readonly CardigannSearchQuery Query = new() { Term = "ubuntu" };

    // One table row per release: a title link and a download link.
    private static CardigannDefinition HtmlDefinition(string search = "") => CardigannDefinitionLoader.Load(CardigannDefinitionLoaderTests.Minimal.Replace(
        "search:\n  path: browse.php\n  rows:\n    selector: tr\n  fields:\n    title:\n      selector: a",
        "search:\n  path: browse.php\n" + search + "  rows:\n    selector: tr\n  fields:\n    title:\n      selector: a.title\n    download:\n      selector: a.dl\n      attribute: href\n    seeders:\n      selector: td.seeds",
        StringComparison.Ordinal));

    private static async Task<CardigannFailure> FailAsync(Func<HttpRequestMessage, HttpResponseMessage> respond, CardigannDefinition? definition = null) =>
        Failure(await Searcher(new StubHttpHandler(respond)).SearchAsync(definition ?? HtmlDefinition(), null, Query, TestContext.Current.CancellationToken));

    [Fact]
    public async Task CloudflareChallengeIsReported()
    {
        CardigannFailure failure = await FailAsync(_ =>
        {
            HttpResponseMessage response = new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("<html><head><title>Just a moment...</title></head></html>") };
            response.Headers.TryAddWithoutValidation("Server", "cloudflare");
            return response;
        });

        Assert.Equal(CardigannFailureKind.CloudflareProtected, failure.Kind);
    }

    [Fact]
    public async Task RedirectIsReportedWhenThePathDoesNotFollowIt()
    {
        CardigannFailure failure = await FailAsync(_ => new HttpResponseMessage(HttpStatusCode.MovedPermanently) { Headers = { Location = new Uri("https://new.example/browse.php") } });

        Assert.Equal(CardigannFailureKind.Redirected, failure.Kind);
        Assert.Contains("https://new.example/browse.php", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FollowRedirectPathFollowsIt()
    {
        CardigannDefinition definition = HtmlDefinition();
        definition = definition with { Search = definition.Search with { Paths = [definition.Search.Paths[0] with { FollowRedirect = true }] } };
        StubHttpHandler handler = new(request => request.RequestUri!.Host == "test.example"
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://new.example/browse.php") } }
            : StubHttpHandler.Ok("""<table><tr><td><a class="title">Ubuntu</a><a class="dl" href="/dl/1.torrent"></a></td><td class="seeds">5</td></tr></table>"""));

        ReleaseInfo release = Assert.Single(Releases(await Searcher(handler).SearchAsync(definition, null, Query, TestContext.Current.CancellationToken)));

        // Relative links resolve against the page that was finally served.
        Assert.Equal("https://new.example/dl/1.torrent", release.DownloadUrl);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ErrorStatusIsReported(HttpStatusCode status)
    {
        Assert.Equal(CardigannFailureKind.HttpError, (await FailAsync(_ => new HttpResponseMessage(status))).Kind);
    }

    [Fact]
    public async Task ConnectionErrorIsReported()
    {
        Assert.Equal(CardigannFailureKind.HttpError, (await FailAsync(_ => throw new HttpRequestException("No such host is known."))).Kind);
    }

    [Fact]
    public async Task SlowSiteTimesOut()
    {
        using HangingHandler handler = new();

        CardigannFailure failure = Failure(await Searcher(handler, TimeSpan.FromMilliseconds(50)).SearchAsync(HtmlDefinition(), null, Query, TestContext.Current.CancellationToken));

        Assert.Equal(CardigannFailureKind.Timeout, failure.Kind);
    }

    [Fact]
    public async Task CallerCancellationThrows()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        using HangingHandler handler = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Searcher(handler).SearchAsync(HtmlDefinition(), null, Query, cancellation.Token));
    }

    [Fact]
    public async Task InvalidJsonIsReported()
    {
        Assert.Equal(CardigannFailureKind.InvalidResponse, (await FailAsync(_ => StubHttpHandler.Ok("<html>maintenance</html>"), Definition("yts.yml"))).Kind);
    }

    [Theory]
    [InlineData("search:\n", "login:\n  path: login.php\n  method: post\n  inputs:\n    username: \"{{ .Config.username }}\"\n    password: \"{{ .Config.password }}\"\nsearch:\n")]
    [InlineData("search:\n", "settings:\n  - name: apikey\n    type: text\nsearch:\n  headers:\n    x-api-key: [\"{{ .Config.apikey }}\"]\n")]
    public async Task LoginsWithoutCredentialsAreUnsupported(string from, string to)
    {
        CardigannDefinition definition = CardigannDefinitionLoader.Load(CardigannDefinitionLoaderTests.Minimal.Replace(from, to, StringComparison.Ordinal));
        StubHttpHandler handler = new(_ => throw new InvalidOperationException("must not be called"));

        Assert.False(CardigannSearcher.IsSupported(definition));
        Assert.Equal(CardigannFailureKind.Unsupported, Failure(await Searcher(handler).SearchAsync(definition, null, Query, TestContext.Current.CancellationToken)).Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnusableRowIsSkippedAndBadFieldKeepsTheRow()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Ok("""
            <table>
            <tr><td><a class="title">Good</a><a class="dl" href="/dl/1.torrent"></a></td><td class="seeds">5</td></tr>
            <tr><td><a class="dl" href="/dl/2.torrent"></a></td><td class="seeds">5</td></tr>
            <tr><td><a class="title">No seeders</a><a class="dl" href="/dl/3.torrent"></a></td></tr>
            </table>
            """));

        IReadOnlyList<ReleaseInfo> releases = Releases(await Searcher(handler).SearchAsync(HtmlDefinition(), null, Query, TestContext.Current.CancellationToken));

        Assert.Equal(["Good", "No seeders"], releases.Select(release => release.Title));
        Assert.Equal("https://test.example/dl/1.torrent", releases[0].DownloadUrl);
        Assert.Equal(5, releases[0].Seeders);
        Assert.Null(releases[1].Seeders);
    }

    [Fact]
    public async Task PostPathSendsInputsAsFormInTheSiteEncoding()
    {
        CardigannDefinition definition = HtmlDefinition("  inputs:\n    q: \"{{ .Keywords }}\"\n    cat: \"\"\n") with { Encoding = "windows-1252" };
        definition = definition with { Search = definition.Search with { Paths = [definition.Search.Paths[0] with { Method = "post" }] } };
        StubHttpHandler handler = new(_ => StubHttpHandler.Ok("<table></table>"));

        Assert.Empty(Releases(await Searcher(handler).SearchAsync(definition, null, new CardigannSearchQuery { Term = "café crème" }, TestContext.Current.CancellationToken)));

        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://test.example/browse.php", request.Url.AbsoluteUri);
        Assert.Equal("q=caf%E9+cr%E8me", request.Body);
    }

    [Fact]
    public async Task DownloadSelectorsThatMatchNothingAreReported()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Ok("<html><body>gone</body></html>"));

        CardigannDownloadResult result = await Searcher(handler).ResolveDownloadAsync(Definition("1337x.yml"), null, new Uri("https://1337x.to/torrent/1/x/"), TestContext.Current.CancellationToken);

        Assert.Equal(CardigannFailureKind.DownloadNotFound, Assert.IsType<CardigannDownloadResult.Failed>(result).Failure.Kind);
    }

    [Fact]
    public async Task DownloadWithoutDownloadBlockIsTheLinkItself()
    {
        StubHttpHandler handler = new(_ => throw new InvalidOperationException("must not be called"));
        Uri link = new("https://yts.gg/torrent/download/CE9156EB497762F8B7577B71C0647A4B0C3423E1");

        CardigannDownloadResult result = await Searcher(handler).ResolveDownloadAsync(Definition("yts.yml"), null, link, TestContext.Current.CancellationToken);

        Assert.Equal(link, Assert.IsType<CardigannDownloadResult.Resolved>(result).Link);
    }

    // Never answers: waits for the request to be cancelled.
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
