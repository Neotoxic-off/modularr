using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;

namespace Modularr.Core.Indexers.Catalog;

// Serves the cached catalog at once, then syncs definitions on startup and every DefinitionsUpdateInterval (Prowlarr's
// ApplicationStartedEvent + scheduled IndexerDefinitionUpdateCommand); inject this to read the catalog.
public sealed class IndexerCatalogHost(IOptions<ModularrOptions> options, IHttpClientFactory httpClientFactory) : BackgroundService
{
    private volatile IndexerCatalog _catalog = IndexerCatalog.Empty;
    private volatile string? _lastSyncError;

    public IndexerCatalog Catalog => _catalog;

    // Null once the last sync succeeded; the cached set stays in use while it fails.
    public string? LastSyncError => _lastSyncError;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        Reload();
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IndexerOptions indexers = options.Value.Indexers;
        using HttpClient http = httpClientFactory.CreateClient(nameof(IndexerCatalogHost));
        using PeriodicTimer timer = new(indexers.DefinitionsUpdateInterval);
        do
        {
            _lastSyncError = await IndexerDefinitionSync.SyncAsync(http, indexers.DefinitionsDirectory, stoppingToken);
            if (_lastSyncError is null)
            {
                Reload();
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private void Reload() => _catalog = IndexerCatalog.Load(options.Value.Indexers);
}
