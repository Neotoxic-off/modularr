using System.Net;
using Modularr.Core.Metadata;

namespace Modularr.Core.Discovery;

// TVmaze's whole show index, the keyless source that lets series be filtered and sorted here. It is read page by page in the
// background (TVmaze allows 20 requests per 10 s) and kept for a day; the first load serves what it has read so far.
public sealed class ShowIndex(Func<int, CancellationToken, Task<IReadOnlyList<IndexedShow>?>> fetchPage, TimeSpan pageDelay, CancellationToken stopping)
{
    // About twice TVmaze's index today: a bound on memory and requests should the source misbehave.
    public const int MaxPages = 700;
    private const int RateLimitRetries = 3;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RateLimitPause = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private volatile IReadOnlyList<IndexedShow> _shows = [];
    private DateTime _nextLoad = DateTime.MinValue;
    private Task _loading = Task.CompletedTask;

    // Why the last load stopped early, null once one completes.
    public string? LastError { get; private set; }

    // The shows read so far, and whether a load is running. Starts the first load, and a reload once the index is a day old.
    public (IReadOnlyList<IndexedShow> Shows, bool Loading) Current()
    {
        lock (_gate)
        {
            if (_loading.IsCompleted && DateTime.UtcNow >= _nextLoad)
            {
                _nextLoad = DateTime.MaxValue;
                _loading = Task.Run(LoadAsync, stopping);
            }

            return (_shows, !_loading.IsCompleted);
        }
    }

    // The running load, for tests.
    public Task Loading
    {
        get
        {
            lock (_gate)
            {
                return _loading;
            }
        }
    }

    private async Task LoadAsync()
    {
        bool first = _shows.Count == 0;
        List<IndexedShow> shows = [];
        try
        {
            for (int page = 0; page < MaxPages && await FetchAsync(page).ConfigureAwait(false) is { } items; page++)
            {
                shows.AddRange(items);
                if (first)
                {
                    _shows = [.. shows];
                }

                await Task.Delay(pageDelay, stopping).ConfigureAwait(false);
            }

            _shows = [.. shows];
            LastError = null;
            Schedule(Lifetime);
        }
        catch (Exception ex) when (!stopping.IsCancellationRequested)
        {
            // A reload that broke keeps the previous index; the first one keeps what it read.
            LastError = ex.Message;
            Schedule(RetryAfter);
        }
    }

    private async Task<IReadOnlyList<IndexedShow>?> FetchAsync(int page)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await fetchPage(page, stopping).ConfigureAwait(false);
            }
            catch (MetadataSourceException ex) when (attempt < RateLimitRetries && ex.InnerException is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
            {
                await Task.Delay(RateLimitPause, stopping).ConfigureAwait(false);
            }
        }
    }

    private void Schedule(TimeSpan after)
    {
        lock (_gate)
        {
            _nextLoad = DateTime.UtcNow + after;
        }
    }
}
