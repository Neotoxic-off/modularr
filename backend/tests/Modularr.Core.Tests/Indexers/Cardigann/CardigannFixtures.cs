using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Cardigann;
using Modularr.Core.Indexers.Definitions;

namespace Modularr.Core.Tests.Indexers.Cardigann;

// Definitions copied from Prowlarr/Indexers v11; responses recorded from the sites (YTS, TPB, showRSS) or rebuilt from their markup (1337x, EZTV, Nyaa).
internal static class CardigannFixtures
{
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Indexers", "Cardigann", "Fixtures", name));

    // requestDelay would only slow the offline tests down.
    public static CardigannDefinition Definition(string name) => CardigannDefinitionLoader.Load(Read(name)) with { RequestDelay = null };

    public static CardigannSearcher Searcher(HttpMessageHandler handler, TimeSpan? timeout = null) =>
        new(new HttpClient(handler), Options.Create(new ModularrOptions { Indexers = new IndexerOptions { RequestTimeout = timeout ?? TimeSpan.FromSeconds(30) } }));

    public static IReadOnlyList<ReleaseInfo> Releases(CardigannSearchResult result) =>
        Assert.IsType<CardigannSearchResult.Found>(result).Releases;

    public static CardigannFailure Failure(CardigannSearchResult result) =>
        Assert.IsType<CardigannSearchResult.Failed>(result).Failure;
}
