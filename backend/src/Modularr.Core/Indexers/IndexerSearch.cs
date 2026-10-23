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
    // Tests pass includeCoolingDown: they are how a cooling-down indexer shows it answers again.
    public async Task<IndexerSearchResult> SearchAsync(
        IEnumerable<CardigannDefinition> definitions, CardigannSearchQuery query, CancellationToken cancellationToken, bool includeCoolingDown = false)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CardigannDefinition[] all = [.. definitions.Where(d => includeCoolingDown || !health.IsCoolingDown(d, now))];
        (IReadOnlyList<ReleaseInfo> Releases, IndexerError? Error)[] outcomes =
            await Task.WhenAll(all.Select(definition => SearchOneAsync(definition, query, cancellationToken))).ConfigureAwait(false);

        return new IndexerSearchResult(
            [.. outcomes.SelectMany(outcome => outcome.Releases)],
            [.. outcomes.Select(outcome => outcome.Error).OfType<IndexerError>()],
            all.Length);
    }

    // Every mirror of the indexer, in parallel: mirrors do not all carry the same catalogue. Their releases are merged without
    // duplicates; the indexer only fails when no mirror answered.
    private async Task<(IReadOnlyList<ReleaseInfo> Releases, IndexerError? Error)> SearchOneAsync(
        CardigannDefinition definition, CardigannSearchQuery query, CancellationToken cancellationToken)
    {
        string active = health.ActiveBaseUrl(definition);
        string[] mirrors = [active, .. health.Candidates(definition).Where(url => url != active)];
        (IReadOnlyList<ReleaseInfo>? Releases, CardigannFailure? Failure)[] results =
            await Task.WhenAll(mirrors.Select(url => SearchMirrorAsync(definition, url, query, cancellationToken))).ConfigureAwait(false);

        if (results.All(r => r.Releases is null))
        {
            CardigannFailure failure = results[0].Failure!;
            return ([], new IndexerError(definition.Id, definition.Name, failure.Kind, failure.Message));
        }

        return ([.. results.SelectMany(r => r.Releases ?? []).DistinctBy(DuplicateKey)], null);
    }

    private async Task<(IReadOnlyList<ReleaseInfo>? Releases, CardigannFailure? Failure)> SearchMirrorAsync(
        CardigannDefinition definition, string baseUrl, CardigannSearchQuery query, CancellationToken cancellationToken)
    {
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

                return (null, failure);

            default:
                throw new InvalidOperationException($"Unknown search result {result}.");
        }
    }

    // The same torrent seen on two mirrors: by info hash when the site gives one, otherwise by title and size.
    internal static string DuplicateKey(ReleaseInfo release) =>
        release.InfoHash is { Length: > 0 } hash ? hash.ToUpperInvariant() : $"{release.Title.Trim().ToUpperInvariant()}|{release.Size}";

    // Failures a mirror can cure; a definition or response the engine cannot use stays broken on every domain.
    private static bool IsDomainFailure(CardigannFailureKind kind) =>
        kind is CardigannFailureKind.Timeout or CardigannFailureKind.HttpError or CardigannFailureKind.Redirected or CardigannFailureKind.CloudflareProtected;
}
