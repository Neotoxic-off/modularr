using Microsoft.Extensions.Logging.Abstractions;
// Behavior from Prowlarr IndexerDefinitionUpdateService (no upstream tests exist for it): master/12/package.zip, cache kept on failure.
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Catalog;
using Modularr.Core.Indexers.Definitions;
using Modularr.Core.Tests.Library;

namespace Modularr.Core.Tests.Indexers.Catalog;

public sealed class IndexerDefinitionSyncTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("modularr-sync-").FullName;

    private string Definitions => Path.Combine(_root, "indexers");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly Dictionary<string, string> Package = new()
    {
        ["1337x.yml"] = CatalogTestFiles.Fixture("1337x.yml"),
        ["shakaw.yml"] = CatalogTestFiles.Fixture("shakaw.yml"),
    };

    private async Task<string?> SyncAsync(StubHandler handler)
    {
        using HttpClient http = new(handler);
        return await IndexerDefinitionSync.SyncAsync(http, Definitions, TestContext.Current.CancellationToken);
    }

    private void WriteCache() => CatalogTestFiles.Write(Definitions, new Dictionary<string, string> { ["cached.yml"] = CatalogTestFiles.SemiPrivateVideo });

    private string[] CachedFiles() => [.. Directory.EnumerateFiles(Definitions).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

    // The first sync runs in the background after StartAsync.
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private void AssertNoLeftovers()
    {
        Assert.False(Directory.Exists(Definitions + ".new"));
        Assert.False(Directory.Exists(Definitions + ".old"));
    }

    [Fact]
    public async Task DownloadsTheProwlarrPackageForSchema12()
    {
        StubHandler handler = StubHandler.Serving(CatalogTestFiles.Zip(Package));

        Assert.Null(await SyncAsync(handler));

        Assert.Equal(new Uri("https://indexers.prowlarr.com/master/12/package.zip"), Assert.Single(handler.Requests));
        Assert.Equal(["1337x.yml", "shakaw.yml"], CachedFiles());
        AssertNoLeftovers();
    }

    [Fact]
    public async Task ReplacesTheWholeCachedSet()
    {
        WriteCache();

        Assert.Null(await SyncAsync(StubHandler.Serving(CatalogTestFiles.Zip(Package))));

        // A definition removed upstream disappears instead of lingering.
        Assert.Equal(["1337x.yml", "shakaw.yml"], CachedFiles());
        AssertNoLeftovers();
    }

    public static TheoryData<string> Failures => ["offline", "server error", "corrupt zip", "empty package"];

    private static StubHandler Failing(string failure) => failure switch
    {
        "offline" => StubHandler.Offline(),
        "server error" => new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
        "corrupt zip" => StubHandler.Serving("<html>maintenance</html>"u8.ToArray()),
        _ => StubHandler.Serving(CatalogTestFiles.Zip(new Dictionary<string, string> { ["README.md"] = "nothing" })),
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task KeepsTheCachedSetWhenTheSyncFails(string failure)
    {
        WriteCache();

        string? error = await SyncAsync(Failing(failure));

        Assert.StartsWith("Indexer definition update failed:", error, StringComparison.Ordinal);
        Assert.Equal(["cached.yml"], CachedFiles());
        AssertNoLeftovers();
    }

    [Fact]
    public async Task SyncsTheLiveProwlarrPackage()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("MODULARR_LIVE_TESTS") != "1", "Set MODULARR_LIVE_TESTS=1 to download the real definition package.");
        using HttpClient http = new();

        Assert.Null(await IndexerDefinitionSync.SyncAsync(http, Definitions, TestContext.Current.CancellationToken));

        IndexerCatalog catalog = IndexerCatalog.Load(new IndexerOptions { DefinitionsDirectory = Definitions });
        Assert.Empty(catalog.Errors);
        Assert.Contains(catalog.Entries, entry => entry is { Id: "1337x", Enabled: true });
        Assert.All(catalog.Entries.Where(entry => entry.Enabled), entry => Assert.Equal(IndexerPrivacy.Public, entry.Privacy));
    }

    [Fact]
    public async Task FirstRunOfflineGivesAnEmptyCatalog()
    {
        Assert.NotNull(await SyncAsync(StubHandler.Offline()));

        Assert.False(Directory.Exists(Definitions));
        Assert.Empty(IndexerCatalog.Load(new IndexerOptions { DefinitionsDirectory = Definitions }).Entries);
    }

    [Fact]
    public async Task RestoresTheCacheLeftByAnInterruptedSwap()
    {
        CatalogTestFiles.Write(Definitions + ".old", new Dictionary<string, string> { ["cached.yml"] = CatalogTestFiles.SemiPrivateVideo });

        Assert.NotNull(await SyncAsync(StubHandler.Offline()));

        Assert.Equal(["cached.yml"], CachedFiles());
        AssertNoLeftovers();
    }

    [Fact]
    public async Task HostServesTheCachedCatalogWhileOfflineAndDisablesIndexersThatFailTheirTest()
    {
        CatalogTestFiles.Write(Definitions, new Dictionary<string, string> { ["1337x.yml"] = CatalogTestFiles.Fixture("1337x.yml") });
        FixedOptionsMonitor options = new(new ModularrOptions { Indexers = new IndexerOptions { DefinitionsDirectory = Definitions } });
        // No IndexerSearch can be resolved: every test fails, like an indexer that does not answer.
        using IndexerCatalogHost host = new(options, new StubHttpClientFactory(StubHandler.Offline()), new ServiceCollection().BuildServiceProvider(), NullLogger<IndexerCatalogHost>.Instance);

        await host.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(host.Catalog.Entries).Enabled);
        await WaitUntilAsync(() => host.LastSyncError is not null && host.IsFailing("1337x"));
        await host.StopAsync(TestContext.Current.CancellationToken);

        // Still listed from the cache, but disabled until a later test (or a manual one) succeeds.
        Assert.False(Assert.Single(host.Catalog.Entries).Enabled);
        host.Report("1337x", works: true);
        Assert.True(Assert.Single(host.Catalog.Entries).Enabled);
    }

    [Fact]
    public async Task HostLoadsTheDownloadedCatalog()
    {
        FixedOptionsMonitor options = new(new ModularrOptions { Indexers = new IndexerOptions { DefinitionsDirectory = Definitions } });
        using IndexerCatalogHost host = new(options, new StubHttpClientFactory(StubHandler.Serving(CatalogTestFiles.Zip(Package))), new ServiceCollection().BuildServiceProvider(), NullLogger<IndexerCatalogHost>.Instance);

        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => host.Catalog.Entries.Count > 0);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["1337x", "shakaw"], host.Catalog.Entries.Select(entry => entry.Id));
        Assert.Null(host.LastSyncError);
    }
}
