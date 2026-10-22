using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Downloads.Torrents;
using Modularr.Core.Persistence;

namespace Modularr.Core.Downloads;

// Upstream RefreshMonitoredDownloads: polls the grabbed torrents, imports (or blocklists) each one once it completes, and fails
// and replaces the ones the content check rejected or that stalled.
// shortcut: polling every 30 s like Sonarr's queue refresh, switch to MonoTorrent completion events if imports must be immediate.
// shortcut: stall timers live in memory and restart with the app, persist them if restarts hide stalled downloads.
public sealed class DownloadMonitor(IServiceScopeFactory scopes, TorrentEngineHost torrents, IOptions<ModularrOptions> options) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<int, StallState> _stalls = [];

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

        foreach (int gone in _stalls.Keys.Except(ids).ToList())
        {
            _stalls.Remove(gone);
        }

        foreach (int id in ids)
        {
            try
            {
                await CheckAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Any failure (disk, database, torrent engine) blocks this download for a manual retry instead of failing every poll;
                // a download already failed (and blocklisted) stays failed when only its replacement search broke.
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ModularrDbContext>().TrackedDownloads.Where(d => d.Id == id && d.State != TrackedDownloadState.Failed)
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
        FailedDownloadService failed = scope.ServiceProvider.GetRequiredService<FailedDownloadService>();
        switch (torrent)
        {
            case null:
                // Removed from the engine by hand: stop tracking it.
                download.State = TrackedDownloadState.Ignored;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                break;

            case { Status: DownloadItemStatus.Completed }:
                if (await scope.ServiceProvider.GetRequiredService<DownloadImporter>().ImportAsync(download, torrent.OutputPath, cancellationToken).ConfigureAwait(false))
                {
                    await failed.RemoveAndRegrabAsync(download, cancellationToken).ConfigureAwait(false);
                }

                break;

            // Only the content check reports Failed.
            case { Status: DownloadItemStatus.Failed }:
                await failed.FailAsync(download, torrent.Message ?? "Rejected by the content check", cancellationToken).ConfigureAwait(false);
                break;

            // Paused by the user, or a local error (disk full...) that is not the release's fault: not stalled, the timers start over.
            case { Status: DownloadItemStatus.Paused } or { Status: DownloadItemStatus.Warning, HasMetadata: true }:
                _stalls.Remove(id);
                break;

            default:
                StallCheck check = StallDetector.Evaluate(_stalls.GetValueOrDefault(id), torrent, DateTimeOffset.UtcNow, options.Value.Torrents);
                _stalls[id] = check.State;
                if (check.Action == StallAction.Retry)
                {
                    await torrents.Engine.ReannounceAsync(download.InfoHash).ConfigureAwait(false);
                }
                else if (check is { Action: StallAction.GiveUp, Reason: string reason })
                {
                    await failed.FailAsync(download, reason, cancellationToken).ConfigureAwait(false);
                }

                break;
        }
    }
}
