using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Modularr.Core.Search;

// A series (optionally one season) or a movie to search and grab for.
public sealed record SearchJob(int? SeriesId, int? MovieId, int? Season = null);

// When the item was last searched and what came of it ("2 release(s) grabbed", "Nothing grabbed", the failure).
public sealed record SearchOutcome(DateTime Time, string Result);

// Runs the automatic searches one at a time off the HTTP request: an indexer round can take a minute.
// shortcut: queued jobs and outcomes live in memory and are lost on restart, persist them if a restart mid-search matters.
public sealed class SearchRunner(IServiceScopeFactory scopes) : BackgroundService
{
    private readonly Channel<SearchJob> _jobs = Channel.CreateUnbounded<SearchJob>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<(int?, int?), SearchOutcome> _outcomes = [];

    public void Enqueue(SearchJob job) => _jobs.Writer.TryWrite(job);

    public SearchOutcome? LastSearch(int? seriesId, int? movieId) => _outcomes.GetValueOrDefault((seriesId, movieId));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (SearchJob job in _jobs.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            string result;
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                AutoSearch search = scope.ServiceProvider.GetRequiredService<AutoSearch>();
                int grabbed = (job.MovieId is int movieId
                    ? await search.SearchMovieAsync(movieId, stoppingToken).ConfigureAwait(false)
                    : await search.SearchSeriesAsync(job.SeriesId ?? 0, job.Season, DateOnly.FromDateTime(DateTime.UtcNow), stoppingToken).ConfigureAwait(false)).Count;
                result = grabbed == 0 ? "Nothing grabbed" : $"{grabbed} release(s) grabbed";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One failed search (indexer, torrent link, deleted item) must not stop the queue.
                result = $"Failed: {ex.Message}";
            }

            _outcomes[(job.SeriesId, job.MovieId)] = new SearchOutcome(DateTime.UtcNow, result);
        }
    }
}
