using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Downloads.Torrents;
using Modularr.Core.Library;
using Modularr.Core.Persistence;
using Modularr.Core.PostProcessing;

namespace Modularr.Core.Downloads;

// Upstream RefreshMonitoredDownloads: polls the grabbed torrents, imports (or blocklists) each one once it completes, and fails
// and replaces the ones the content check rejected or that stalled.
// shortcut: polling every 30 s like Sonarr's queue refresh, switch to MonoTorrent completion events if imports must be immediate.
// shortcut: stall timers live in memory and restart with the app, persist them if restarts hide stalled downloads.
public sealed class DownloadMonitor(IServiceScopeFactory scopes, TorrentEngineHost torrents, IOptionsMonitor<ModularrOptions> options, ILogger<DownloadMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<int, StallState> _stalls = [];

    // Downloads whose DownloadStarted is recorded; the feed is asked once per download per run, as retention may prune the entry.
    private readonly HashSet<int> _started = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await CheckAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.JobFailed("Download monitor", ex);
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

        _started.IntersectWith(ids);

        foreach (int id in ids)
        {
            try
            {
                await CheckAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.JobFailed("Download monitor", ex);
                // Any failure (disk, database, torrent engine) blocks this download for a manual retry instead of failing every poll;
                // a download already failed (and blocklisted) stays failed when only its replacement search broke.
                await BlockAsync(id, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task BlockAsync(int id, string reason, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ModularrDbContext db = scope.ServiceProvider.GetRequiredService<ModularrDbContext>();
        if (await db.TrackedDownloads.FirstOrDefaultAsync(d => d.Id == id && d.State != TrackedDownloadState.Failed, cancellationToken).ConfigureAwait(false) is TrackedDownload download)
        {
            download.State = TrackedDownloadState.ImportBlocked;
            download.Message = reason;
            scope.ServiceProvider.GetRequiredService<ActivityLog>().Add(Activity.For(download, ActivityKind.ImportFailed, ActivityLevel.Error, reason));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Files a failed import already moved left the downloads folder: the scan records them now rather than at the next library scan.
            await RescanAsync(scope.ServiceProvider.GetRequiredService<DiskScanService>(), download, cancellationToken).ConfigureAwait(false);
        }
    }

    // Picks up what the import replaced, left aside or moved before failing; the download keeps its state whatever happens here.
    private async Task RescanAsync(DiskScanService scans, TrackedDownload download, CancellationToken cancellationToken)
    {
        try
        {
            _ = download.MovieId is int movieId
                ? await scans.ScanMovieAsync(movieId, record: true, cancellationToken).ConfigureAwait(false)
                : await scans.ScanSeriesAsync(download.SeriesId ?? 0, record: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.JobFailed("Download monitor", ex);
            // The periodic library scan catches up.
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
        ActivityLog activities = scope.ServiceProvider.GetRequiredService<ActivityLog>();
        switch (torrent)
        {
            case null:
                // Removed from the engine by hand: stop tracking it.
                download.State = TrackedDownloadState.Ignored;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                break;

            case { Status: DownloadItemStatus.Completed }:
                ModularrOptions settings = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<ModularrOptions>>().CurrentValue;
                bool move = settings.PostProcessing.ImportMode == ImportMode.Move;
                if (move)
                {
                    // Stopped first, so the engine holds none of the files being moved; a failed import leaves it stopped with its data, until
                    // the user retries the import (a stopped complete torrent still reports Completed).
                    await torrents.Engine.PauseAsync(download.InfoHash).ConfigureAwait(false);
                }

                if (await scope.ServiceProvider.GetRequiredService<DownloadImporter>().ImportAsync(download, torrent.OutputPath, cancellationToken).ConfigureAwait(false))
                {
                    await failed.RemoveAndRegrabAsync(download, cancellationToken).ConfigureAwait(false);
                }
                else if (download.State == TrackedDownloadState.Imported)
                {
                    if (move)
                    {
                        // The files left with the move, so the data is not deleted with the torrent: only what is left empty goes.
                        await torrents.Engine.RemoveAsync(download.InfoHash, deleteData: false).ConfigureAwait(false);
                        DownloadImporter.DeleteEmptyFolders(torrent.OutputPath, settings.Paths.Downloads);
                    }

                    await RescanAsync(scope.ServiceProvider.GetRequiredService<DiskScanService>(), download, cancellationToken).ConfigureAwait(false);
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
                if (torrent.HasMetadata && !_started.Contains(id))
                {
                    if (!await db.Activities.AnyAsync(a => a.DownloadId == id && a.Kind == ActivityKind.DownloadStarted, cancellationToken).ConfigureAwait(false))
                    {
                        await activities.RecordAsync(Activity.For(download, ActivityKind.DownloadStarted, ActivityLevel.Info, "Metadata received, downloading"), cancellationToken).ConfigureAwait(false);
                    }

                    _started.Add(id);
                }

                StallCheck check = StallDetector.Evaluate(_stalls.GetValueOrDefault(id), torrent, DateTimeOffset.UtcNow, options.CurrentValue.Torrents);
                _stalls[id] = check.State;
                if (check is { Action: StallAction.Retry, Reason: string stalled })
                {
                    await torrents.Engine.ReannounceAsync(download.InfoHash).ConfigureAwait(false);
                    await activities.RecordAsync(
                        Activity.For(download, ActivityKind.DownloadStalled, ActivityLevel.Warning, $"{stalled}, retry {check.State.Retries} of {options.CurrentValue.Torrents.StallRetries}"),
                        cancellationToken).ConfigureAwait(false);
                }
                else if (check is { Action: StallAction.GiveUp, Reason: string reason })
                {
                    // Missing metadata is not the release's fault for sure: one hostile peer can stall the fetch on every retry, and a
                    // blocklist entry would then ban a good release. It is failed and re-grabbed (bounded) but stays allowed.
                    await failed.FailAsync(download, reason, cancellationToken, blocklist: torrent.HasMetadata).ConfigureAwait(false);
                }

                break;
        }
    }
}
