using Microsoft.Extensions.Logging;
using Modularr.Core.Configuration;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Modularr.Core.Subtitles;

// Fetches subtitles off the import path: each imported file once (DownloadImporter enqueues it), and every RetryInterval the
// files still missing a wanted language, RetryBatch of them at most.
// shortcut: the queue lives in memory, a file imported just before a restart waits for the next periodic retry.
public sealed class SubtitleRunner(IServiceScopeFactory scopes, ILogger<SubtitleRunner> logger) : BackgroundService
{
    private readonly Channel<(int Id, bool ReportMissing)> _files = Channel.CreateUnbounded<(int Id, bool ReportMissing)>(new UnboundedChannelOptions { SingleReader = true });

    // reportMissing: false for a search the user started, which would otherwise log a warning per file still missing a language.
    public void Enqueue(int mediaFileId, bool reportMissing = true) => _files.Writer.TryWrite((mediaFileId, reportMissing));

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(ImportedAsync(stoppingToken), RetryAsync(stoppingToken));

    private async Task ImportedAsync(CancellationToken stoppingToken)
    {
        await foreach ((int id, bool reportMissing) in _files.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunAsync(service => service.DownloadMissingAsync(id, reportMissing, stoppingToken), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RetryAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(SubtitleService.RetryInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunAsync(service => service.RetryMissingAsync(DateTime.UtcNow, stoppingToken), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunAsync(Func<SubtitleService, Task> work, CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await work(scope.ServiceProvider.GetRequiredService<SubtitleService>()).ConfigureAwait(false);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.JobFailed("Subtitle search", ex);
            // A removed file or a busy database must not stop the runner; the periodic retry comes back to it.
        }
    }
}
