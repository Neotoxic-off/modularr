using Microsoft.Extensions.Logging;
using Modularr.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modularr.Core.Downloads.Torrents;
using Modularr.Core.Persistence;

namespace Modularr.Core.Activities;

// Prunes the feed to its retention at startup and every hour.
public sealed class ActivityPruner(IServiceScopeFactory scopes, ILogger<ActivityPruner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ActivityLog>().PruneAsync(DateTime.UtcNow, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.JobFailed("Activity pruning", ex);
                // An exception escaping ExecuteAsync stops the host; a busy database is pruned on the next tick.
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}

// Publishes the running downloads' progress every 2 s, only while a live client listens.
public sealed class DownloadProgressTicker(IServiceScopeFactory scopes, TorrentEngineHost torrents, ActivityFeed feed, ILogger<DownloadProgressTicker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            if (!feed.HasSubscribers)
            {
                continue;
            }

            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.JobFailed("Download progress", ex);
                // The engine not started yet or a busy database: the next tick tries again.
            }
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        List<TrackedDownload> running = await scope.ServiceProvider.GetRequiredService<ModularrDbContext>().TrackedDownloads.AsNoTracking()
            .Where(d => d.State == TrackedDownloadState.Downloading).ToListAsync(cancellationToken).ConfigureAwait(false);
        DownloadProgress[] progress =
        [
            .. running.Select(d => (d.Id, Torrent: torrents.Engine.Get(d.InfoHash)))
                .Where(d => d.Torrent is not null)
                .Select(d => new DownloadProgress(d.Id, d.Torrent!.Progress, d.Torrent.DownloadRate, (long?)d.Torrent.RemainingTime?.TotalSeconds)),
        ];
        if (progress.Length > 0)
        {
            feed.Publish(new ServerEvent(ServerEvent.Progress, progress));
        }
    }
}
