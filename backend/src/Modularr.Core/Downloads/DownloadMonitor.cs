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
            await CheckAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ModularrDbContext db = scope.ServiceProvider.GetRequiredService<ModularrDbContext>();
        DownloadImporter importer = scope.ServiceProvider.GetRequiredService<DownloadImporter>();

        foreach (TrackedDownload download in await db.TrackedDownloads.Where(d => d.State == TrackedDownloadState.Downloading).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            TorrentStatus? torrent = torrents.Engine.Get(download.InfoHash);
            if (torrent is null)
            {
                // Removed from the engine by hand: stop tracking it.
                download.State = TrackedDownloadState.Ignored;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (torrent.Status == DownloadItemStatus.Completed)
            {
                try
                {
                    if (await importer.ImportAsync(download, torrent.OutputPath, cancellationToken).ConfigureAwait(false))
                    {
                        await torrents.Engine.RemoveAsync(download.InfoHash, deleteData: true).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A disk error must not stop the monitor (an unhandled one stops the host); the user retries by hand.
                    download.State = TrackedDownloadState.ImportBlocked;
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
