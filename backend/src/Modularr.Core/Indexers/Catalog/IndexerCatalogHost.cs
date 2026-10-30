using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Indexers.Cardigann;

namespace Modularr.Core.Indexers.Catalog;

// Serves the cached catalog at once, then syncs definitions on startup and every DefinitionsUpdateInterval (Prowlarr's
// ApplicationStartedEvent + scheduled IndexerDefinitionUpdateCommand); inject this to read the catalog.
// After each sync every supported indexer is tested (a search without a term, like Prowlarr's test); the ones that fail are
// disabled until a later check or a manual test succeeds, so searches only go to indexers that answer.
// Indexers whose login is not filled in are disabled, so they are never tested nor reported as failing.
public sealed class IndexerCatalogHost(IOptionsMonitor<ModularrOptions> options, IHttpClientFactory httpClientFactory, IServiceProvider services, ILogger<IndexerCatalogHost> logger) : BackgroundService
{
    private const int ParallelTests = 8;

    private volatile IndexerCatalog _loaded = IndexerCatalog.Empty;
    private volatile IndexerCatalog _catalog = IndexerCatalog.Empty;
    private volatile string? _lastSyncError;
    private ImmutableHashSet<string> _failing = ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

    public IndexerCatalog Catalog => _catalog;

    // Null once the last sync succeeded; the cached set stays in use while it fails.
    public string? LastSyncError => _lastSyncError;

    // Disabled because its last test failed, not by the operator.
    public bool IsFailing(string id) => _failing.Contains(id);

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        Reload();
        return base.StartAsync(cancellationToken);
    }

    // A manual test result counts like a scheduled one.
    public void Report(string id, bool works)
    {
        ImmutableInterlocked.Update(ref _failing, failing => works ? failing.Remove(id) : failing.Add(id));
        Apply();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IndexerOptions indexers = options.CurrentValue.Indexers;
        using HttpClient http = httpClientFactory.CreateClient(nameof(IndexerCatalogHost));
        using PeriodicTimer timer = new(indexers.DefinitionsUpdateInterval);
        do
        {
            _lastSyncError = await IndexerDefinitionSync.SyncAsync(http, indexers.DefinitionsDirectory, stoppingToken);
            if (_lastSyncError is null)
            {
                Reload();
            }

            await TestAllAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TestAllAsync(CancellationToken cancellationToken)
    {
        using SemaphoreSlim slots = new(ParallelTests);
        IndexerCatalogEntry[] candidates = [.. _loaded.Entries.Where(entry => entry.Enabled)];
        bool[] works = await Task.WhenAll(candidates.Select(async entry =>
        {
            await slots.WaitAsync(cancellationToken);
            try
            {
                return await AnyMirrorAnswersAsync(entry, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.JobFailed("Indexer catalog", ex);
                return false;
            }
            finally
            {
                slots.Release();
            }
        }));

        _failing = [.. candidates.Where((_, i) => !works[i]).Select(entry => entry.Id)];
        Apply();
    }

    // The active domain first (through the normal search, which keeps its health), then each other mirror of the definition:
    // the first that answers becomes the active domain, so an indexer whose main site is down keeps working on a mirror.
    private async Task<bool> AnyMirrorAnswersAsync(IndexerCatalogEntry entry, CancellationToken cancellationToken)
    {
        IndexerSearchResult result = await services.GetRequiredService<IndexerSearch>()
            .SearchAsync([entry.Definition], new CardigannSearchQuery(), cancellationToken, includeCoolingDown: true);
        if (result.Errors.Count == 0)
        {
            return true;
        }

        IndexerDomainHealth health = services.GetRequiredService<IndexerDomainHealth>();
        CardigannSearcher searcher = services.GetRequiredService<CardigannSearcher>();
        foreach (string mirror in health.Candidates(entry.Definition).Skip(1))
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.CurrentValue.Indexers.SearchTimeout);
            try
            {
                if (await searcher.SearchAsync(entry.Definition, mirror, new CardigannSearchQuery(), timeout.Token) is CardigannSearchResult.Found)
                {
                    health.Promote(entry.Definition, mirror);
                    return true;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // This mirror did not answer in time: try the next one.
            }
        }

        return false;
    }

    // A definition the loader did not foresee must never stop the app: keep the last good catalog and report why.
    // Also called after an indexer's settings are saved, so a newly configured login is enabled at once.
    public void Reload()
    {
        try
        {
            _loaded = IndexerCatalog.Load(options.CurrentValue.Indexers);
            Apply();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.JobFailed("Indexer catalog", ex);
            _lastSyncError = $"Indexer catalog reload failed: {ex.Message}";
        }
    }

    private void Apply()
    {
        IndexerCatalog loaded = _loaded;
        ImmutableHashSet<string> failing = _failing;
        _catalog = new IndexerCatalog
        {
            Entries = [.. loaded.Entries.Select(entry => entry.Enabled && failing.Contains(entry.Id) ? entry with { Enabled = false } : entry)],
            Errors = loaded.Errors,
        };
    }
}
