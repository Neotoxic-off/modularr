using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modularr.Core.Downloads.Torrents;
using Modularr.Core.Persistence;

namespace Modularr.Core.Downloads;

// Upstream RefreshMonitoredDownloads: polls the grabbed torrents and imports (or blocklists) each one once it completes.
// shortcut: polling every 30 s like Sonarr's queue refresh, switch to MonoTorrent completion events if imports must be immediate.
public sealed class DownloadMonitor(IServiceScopeFactory scopes, TorrentEngineHost torrents) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await CheckAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // An exception escaping ExecuteAsync stops the host; a failed poll (busy database...) is retried on the next tick.
            }
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        List<int> ids;
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            ids = await scope.ServiceProvider.GetRequiredService<ModularrDbContext>().TrackedDownloads
                .Where(d => d.State == TrackedDownloadState.Downloading).Select(d => d.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (int id in ids)
        {
            try
            {
                await CheckAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Any failure (disk, database, torrent engine) blocks this download for a manual retry instead of failing every poll.
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ModularrDbContext>().TrackedDownloads.Where(d => d.Id == id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(d => d.State, TrackedDownloadState.ImportBlocked), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // One scope per download, so a failed SaveChanges does not leave pending changes for the next one.
    private async Task CheckAsync(int id, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ModularrDbContext db = scope.ServiceProvider.GetRequiredService<ModularrDbContext>();
        TrackedDownload download = await db.TrackedDownloads.SingleAsync(d => d.Id == id, cancellationToken).ConfigureAwait(false);
        TorrentStatus? torrent = torrents.Engine.Get(download.InfoHash);
        if (torrent is null)
        {
            // Removed from the engine by hand: stop tracking it.
            download.State = TrackedDownloadState.Ignored;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (torrent.Status == DownloadItemStatus.Completed
            && await scope.ServiceProvider.GetRequiredService<DownloadImporter>().ImportAsync(download, torrent.OutputPath, cancellationToken).ConfigureAwait(false))
        {
            await torrents.Engine.RemoveAsync(download.InfoHash, deleteData: true).ConfigureAwait(false);
        }
    }
}
