using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Indexers.Cardigann;
using Modularr.Core.Indexers.Catalog;
using Modularr.Core.Indexers.Definitions;

namespace Modularr.Core.Indexers;

public sealed record IndexerError(string IndexerId, string Indexer, CardigannFailureKind Kind, string Message);

public sealed record IndexerSearchResult(IReadOnlyList<ReleaseInfo> Releases, IReadOnlyList<IndexerError> Errors, int IndexersSearched);

// Prowlarr's search fan-out: every indexer in parallel, each under its own timeout; one failing indexer never fails the search.
public sealed class IndexerSearch(CardigannSearcher searcher, IndexerDomainHealth health, IOptions<ModularrOptions> options)
{
    public async Task<IndexerSearchResult> SearchAsync(IEnumerable<CardigannDefinition> definitions, CardigannSearchQuery query, CancellationToken cancellationToken)
    {
        CardigannDefinition[] all = [.. definitions];
        (IReadOnlyList<ReleaseInfo> Releases, IndexerError? Error)[] outcomes =
            await Task.WhenAll(all.Select(definition => SearchOneAsync(definition, query, cancellationToken))).ConfigureAwait(false);

        return new IndexerSearchResult(
            [.. outcomes.SelectMany(outcome => outcome.Releases)],
            [.. outcomes.Select(outcome => outcome.Error).OfType<IndexerError>()],
            all.Length);
    }

    private async Task<(IReadOnlyList<ReleaseInfo> Releases, IndexerError? Error)> SearchOneAsync(
        CardigannDefinition definition, CardigannSearchQuery query, CancellationToken cancellationToken)
    {
        string baseUrl = health.ActiveBaseUrl(definition);
        TimeSpan searchTimeout = options.Value.Indexers.SearchTimeout;
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(searchTimeout);

        CardigannSearchResult result;
        try
        {
            result = await searcher.SearchAsync(definition, baseUrl, query, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = new CardigannSearchResult.Failed(new CardigannFailure(CardigannFailureKind.Timeout, $"No answer within {searchTimeout.TotalSeconds:0} s"));
        }

        switch (result)
        {
            case CardigannSearchResult.Found found:
                health.ReportSuccess(definition, baseUrl);
                return (found.Releases, null);

            case CardigannSearchResult.Failed { Failure: CardigannFailure failure }:
                if (IsDomainFailure(failure.Kind))
                {
                    health.ReportFailure(definition, baseUrl, failure.Message);
                }
                else
                {
                    health.ReportError(definition, failure.Message);
                }

                return ([], new IndexerError(definition.Id, definition.Name, failure.Kind, failure.Message));

            default:
                throw new InvalidOperationException($"Unknown search result {result}.");
        }
    }

    // Failures a mirror can cure; a definition or response the engine cannot use stays broken on every domain.
    private static bool IsDomainFailure(CardigannFailureKind kind) =>
        kind is CardigannFailureKind.Timeout or CardigannFailureKind.HttpError or CardigannFailureKind.Redirected or CardigannFailureKind.CloudflareProtected;
}
