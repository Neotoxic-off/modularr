using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Indexers.Cardigann;

namespace Modularr.Core.Tests.Indexers.Cardigann;

// Hits the real sites: run with MODULARR_LIVE_INDEXERS=1 to check the recorded fixtures still match them.
public sealed class CardigannLiveTests
{
    [Theory]
    [InlineData("yts.yml", "Inception")]
    [InlineData("thepiratebay.yml", "ubuntu")]
    [InlineData("showrss.yml", "")]
    public async Task LiveSearchReturnsReleases(string definition, string term)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("MODULARR_LIVE_INDEXERS") == "1", "Set MODULARR_LIVE_INDEXERS=1 to search the live sites.");
        using HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Modularr/1.0");
        CardigannSearcher searcher = new(client, Options.Create(new ModularrOptions()));

        CardigannSearchResult result = await searcher.SearchAsync(CardigannFixtures.Definition(definition), null, new CardigannSearchQuery { Term = term }, TestContext.Current.CancellationToken);

        Assert.NotEmpty(CardigannFixtures.Releases(result));
    }
}
