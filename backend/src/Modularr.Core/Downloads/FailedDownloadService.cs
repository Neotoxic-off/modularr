using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
    DownloadService downloads,
    ReleaseSearch search,
    TorrentEngineHost torrents,
    IOptions<ModularrOptions> options)
{
    public async Task FailAsync(TrackedDownload download, string reason, CancellationToken cancellationToken)
    {
        await importer.FailAsync(download, reason, cancellationToken).ConfigureAwait(false);
        await RemoveAndRegrabAsync(download, cancellationToken).ConfigureAwait(false);
    }

    // For a download already failed and blocklisted.
    public async Task RemoveAndRegrabAsync(TrackedDownload download, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(download);
        await torrents.Engine.RemoveAsync(download.InfoHash, deleteData: true).ConfigureAwait(false);

        if (await RegrabRequestAsync(db, download, options.Value.Torrents.MaxAutomaticRegrabs, cancellationToken).ConfigureAwait(false) is not ReleaseSearchRequest request)
        {
            return;
        }

        ReleaseSearchResult result = await search.SearchAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.Decisions.FirstOrDefault(d => d.Approved) is DownloadDecision best)
        {
            TrackedDownload next = await downloads.GrabAsync(
                new GrabRequest { Release = best.Candidate.Release, SeriesId = download.SeriesId, MovieId = download.MovieId }, cancellationToken).ConfigureAwait(false);
            next.Regrabs = download.Regrabs + 1;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
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
            [Episode episode] => new ReleaseSearchRequest
            {
                TvdbId = tvdbId,
                Season = episode.SeasonNumber,
                Episode = episode.EpisodeNumber.ToString(CultureInfo.InvariantCulture),
            },
            [Episode first, ..] when episodes.All(e => e.SeasonNumber == first.SeasonNumber) => new ReleaseSearchRequest { TvdbId = tvdbId, Season = first.SeasonNumber },
            _ => null,
        };
    }
}
