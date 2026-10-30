using System.Net;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Cardigann;
using Modularr.Core.Indexers.Catalog;
using Modularr.Core.Indexers.Definitions;
using Modularr.Core.Tests.Indexers.Cardigann;

namespace Modularr.Core.Tests.Indexers;

public sealed class IndexerSearchTests
{
    private static readonly CardigannSearchQuery Query = new() { Type = CardigannSearchType.MovieSearch, Term = "Inception", ImdbId = 1375666 };

    [Fact]
    public async Task OneFailingIndexerIsReportedWithoutFailingTheSearch()
    {
        StubHttpHandler handler = new(request => request.RequestUri!.Host == "apibay.org"
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : StubHttpHandler.Fixture("yts-inception.json"));
        (IndexerSearch search, IndexerDomainHealth health) = Create(handler, TimeSpan.FromSeconds(30));
        CardigannDefinition yts = CardigannFixtures.Definition("yts.yml");
        CardigannDefinition tpb = CardigannFixtures.Definition("thepiratebay.yml");

        IndexerSearchResult result = await search.SearchAsync([yts, tpb], Query, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.IndexersSearched);
        Assert.Equal(3, result.Releases.Count);
        Assert.All(result.Releases, release => Assert.Equal("yts", release.IndexerId));
        IndexerError error = Assert.Single(result.Errors);
        Assert.Equal(("thepiratebay", CardigannFailureKind.HttpError), (error.IndexerId, error.Kind));

        IndexerHealth tpbHealth = health.Health(tpb);
        Assert.Equal(1, tpbHealth.Failures);
        Assert.Equal(error.Message, tpbHealth.LastError);
        IndexerHealth ytsHealth = health.Health(yts);
        Assert.Equal((0, null), (ytsHealth.Failures, ytsHealth.LastError));
        Assert.NotNull(ytsHealth.LastCheck);
    }

    [Fact]
    public async Task ASlowIndexerTimesOutAndCountsAsADomainFailure()
    {
        (IndexerSearch search, IndexerDomainHealth health) = Create(new SlowHandler(), TimeSpan.FromMilliseconds(200));
        CardigannDefinition yts = CardigannFixtures.Definition("yts.yml");

        IndexerSearchResult result = await search.SearchAsync([yts], Query, TestContext.Current.CancellationToken);

        Assert.Empty(result.Releases);
        Assert.Equal(CardigannFailureKind.Timeout, Assert.Single(result.Errors).Kind);
        Assert.Equal(1, health.Health(yts).Failures);
    }

    [Fact]
    public async Task EveryMirrorIsSearchedAndTheSameReleasesAreNotDoubled()
    {
        // Mirrors may carry different catalogues: all are asked, and a release several list appears once.
        StubHttpHandler handler = new(_ => StubHttpHandler.Fixture("yts-inception.json"));
        (IndexerSearch search, IndexerDomainHealth health) = Create(handler, TimeSpan.FromSeconds(30));
        CardigannDefinition yts = CardigannFixtures.Definition("yts.yml");

        IndexerSearchResult result = await search.SearchAsync([yts], Query, TestContext.Current.CancellationToken);

        Assert.True(health.Candidates(yts).Count > 1);
        Assert.Equal(health.Candidates(yts).Count, handler.Requests.Count);
        Assert.Equal(3, result.Releases.Count);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task OneMirrorAnsweringIsEnough()
    {
        int calls = 0;
        StubHttpHandler handler = new(_ => Interlocked.Increment(ref calls) == 1
            ? StubHttpHandler.Fixture("yts-inception.json")
            : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        (IndexerSearch search, _) = Create(handler, TimeSpan.FromSeconds(30));

        IndexerSearchResult result = await search.SearchAsync([CardigannFixtures.Definition("yts.yml")], Query, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Releases.Count);
        Assert.Empty(result.Errors);
    }

    private static (IndexerSearch Search, IndexerDomainHealth Health) Create(HttpMessageHandler handler, TimeSpan searchTimeout)
    {
        IOptions<ModularrOptions> options = Options.Create(new ModularrOptions { Indexers = new IndexerOptions { SearchTimeout = searchTimeout } });
        IndexerDomainHealth health = new(options);
        return (new IndexerSearch(new CardigannSearcher(new HttpClient(handler), new Library.FixedOptionsMonitor(options.Value), new CardigannSessions()), health, options), health);
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
