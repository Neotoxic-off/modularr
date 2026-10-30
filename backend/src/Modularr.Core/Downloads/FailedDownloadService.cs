using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Downloads.Torrents;
using Modularr.Core.Persistence;
using Modularr.Core.Search;

namespace Modularr.Core.Downloads;

// Upstream FailedDownloadService + RedownloadFailedDownloadService: a failed release is blocklisted and removed with its data,
// then the series or movie is searched again and the best approved release grabbed; the blocklist excludes the failed one.
public sealed class FailedDownloadService(
    ModularrDbContext db,
    DownloadImporter importer,
    AutoSearch search,
    TorrentEngineHost torrents,
    ActivityLog activities,
    IOptionsMonitor<ModularrOptions> options)
{
    public async Task FailAsync(TrackedDownload download, string reason, CancellationToken cancellationToken, bool blocklist = true)
    {
        ArgumentNullException.ThrowIfNull(download);

        // Removed before it is saved Failed: a failed download is never polled again, so a torrent left behind would keep running.
        await torrents.Engine.RemoveAsync(download.InfoHash, deleteData: true).ConfigureAwait(false);
        await importer.FailAsync(download, reason, cancellationToken, blocklist).ConfigureAwait(false);
        await RegrabAsync(download, cancellationToken).ConfigureAwait(false);
    }

    // For a download already failed and blocklisted.
    public async Task RemoveAndRegrabAsync(TrackedDownload download, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(download);
        await torrents.Engine.RemoveAsync(download.InfoHash, deleteData: true).ConfigureAwait(false);
        await RegrabAsync(download, cancellationToken).ConfigureAwait(false);
    }

    // shortcut: a search that throws is not retried and the download stays failed, add a pending re-grab flag if indexers fail often.
    private async Task RegrabAsync(TrackedDownload download, CancellationToken cancellationToken)
    {
        int maxRegrabs = options.CurrentValue.Torrents.MaxAutomaticRegrabs;
        if (await RegrabRequestAsync(db, download, maxRegrabs, cancellationToken).ConfigureAwait(false) is not ReleaseSearchRequest request)
        {
            if (download.Regrabs >= maxRegrabs)
            {
                await activities.RecordAsync(
                    Activity.For(download, ActivityKind.NothingFound, ActivityLevel.Warning, "Regrab limit reached, no replacement will be tried"), cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        GrabResult result = await search.GrabBestAsync(request, download.SeriesId, download.MovieId, wanted: null, cancellationToken).ConfigureAwait(false);
        foreach (TrackedDownload next in result.Grabbed)
        {
            next.Regrabs = download.Regrabs + 1;
        }

        activities.Add(result.Grabbed.Count > 0
            ? Activity.For(download, ActivityKind.Regrabbed, ActivityLevel.Info, $"Replaced by {string.Join(", ", result.Grabbed.Select(d => d.ReleaseTitle))}")
            : Activity.For(download, ActivityKind.NothingFound, ActivityLevel.Warning, $"No replacement: {result.Releases} release(s) found, {result.Approved} approved"));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // The search that finds a replacement: the movie, or the series episode (or season) the failed download was for.
    // Null once the re-grab cap is reached, or when the episodes are unknown or span several seasons.
    public static async Task<ReleaseSearchRequest?> RegrabRequestAsync(ModularrDbContext db, TrackedDownload download, int maxRegrabs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(download);
        if (download.Regrabs >= maxRegrabs)
        {
            return null;
        }

        if (download.MovieId is int movieId)
        {
            return await db.Movies.AsNoTracking().Where(m => m.Id == movieId)
                .Select(m => new ReleaseSearchRequest { Type = MediaType.Movie, TmdbId = m.TmdbId }).SingleAsync(cancellationToken).ConfigureAwait(false);
        }

        int[] episodeIds = [.. download.EpisodeIds];
        List<Episode> episodes = await db.Episodes.AsNoTracking().Where(e => episodeIds.Contains(e.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        int tvdbId = await db.Series.Where(s => s.Id == download.SeriesId).Select(s => s.TvdbId).SingleAsync(cancellationToken).ConfigureAwait(false);
        return episodes switch
        {
            [Episode episode] => ReleaseSearchRequest.ForEpisode(tvdbId, episode),
            [Episode first, ..] when episodes.All(e => e.SeasonNumber == first.SeasonNumber) => ReleaseSearchRequest.ForSeason(tvdbId, first.SeasonNumber),
            _ => null,
        };
    }
}
