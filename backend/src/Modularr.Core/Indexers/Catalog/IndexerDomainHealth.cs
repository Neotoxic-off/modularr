using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Indexers.Definitions;

namespace Modularr.Core.Indexers.Catalog;

// In-memory active domain per indexer: the search engine reports each request, and repeated failures move to the next link.
public sealed class IndexerDomainHealth(IOptions<ModularrOptions> options)
{
    public const int FailoverThreshold = 3;

    private readonly ConcurrentDictionary<string, DomainState> _states = new(StringComparer.OrdinalIgnoreCase);

    public string ActiveBaseUrl(CardigannDefinition definition)
    {
        IReadOnlyList<string> candidates = Candidates(definition);
        DomainState state = State(definition, candidates);
        lock (state)
        {
            return state.ActiveUrl;
        }
    }

    public IndexerHealth Health(CardigannDefinition definition)
    {
        DomainState state = State(definition, Candidates(definition));
        lock (state)
        {
            return new IndexerHealth(state.ActiveUrl, state.Failures, state.LastError, state.LastCheck);
        }
    }

    public void ReportSuccess(CardigannDefinition definition, string baseUrl)
    {
        DomainState state = State(definition, Candidates(definition));
        lock (state)
        {
            state.LastError = null;
            state.LastCheck = DateTimeOffset.UtcNow;
            if (state.ActiveUrl == baseUrl)
            {
                state.Failures = 0;
            }
        }
    }

    // A report for a domain that is no longer active (a request in flight during failover) is ignored.
    public void ReportFailure(CardigannDefinition definition, string baseUrl, string? error = null)
    {
        IReadOnlyList<string> candidates = Candidates(definition);
        DomainState state = State(definition, candidates);
        lock (state)
        {
            state.LastError = error ?? "request failed";
            state.LastCheck = DateTimeOffset.UtcNow;
            if (state.ActiveUrl != baseUrl || ++state.Failures < FailoverThreshold)
            {
                return;
            }

            int index = IndexOf(candidates, state.ActiveUrl);
            state.ActiveUrl = candidates[(index + 1) % candidates.Count];
            state.Failures = 0;
        }
    }

    // A failure the domain is not to blame for (the definition or response cannot be used): shown, no failover.
    public void ReportError(CardigannDefinition definition, string error)
    {
        DomainState state = State(definition, Candidates(definition));
        lock (state)
        {
            state.LastError = error;
            state.LastCheck = DateTimeOffset.UtcNow;
        }
    }

    private IReadOnlyList<string> Candidates(CardigannDefinition definition) =>
        definition.CandidateBaseUrls(options.Value.Indexers.BaseUrls.GetValueOrDefault(definition.Id));

    private DomainState State(CardigannDefinition definition, IReadOnlyList<string> candidates)
    {
        DomainState state = _states.GetOrAdd(definition.Id, static (_, first) => new DomainState { ActiveUrl = first }, candidates[0]);
        lock (state)
        {
            // An updated definition may have dropped the active link.
            if (IndexOf(candidates, state.ActiveUrl) < 0)
            {
                state.ActiveUrl = candidates[0];
                state.Failures = 0;
            }
        }

        return state;
    }

    private static int IndexOf(IReadOnlyList<string> candidates, string url)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i] == url)
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class DomainState
    {
        public required string ActiveUrl { get; set; }
        public int Failures { get; set; }
        public string? LastError { get; set; }
        public DateTimeOffset? LastCheck { get; set; }
    }
}

// Failures counts consecutive failures on the active domain; LastCheck is null until the indexer was searched.
public sealed record IndexerHealth(string ActiveUrl, int Failures, string? LastError, DateTimeOffset? LastCheck);
