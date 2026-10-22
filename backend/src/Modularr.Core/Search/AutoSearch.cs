using Microsoft.EntityFrameworkCore;
using Modularr.Core.Decisions;
using Modularr.Core.Downloads;
using Modularr.Core.Persistence;

namespace Modularr.Core.Search;

// Upstream MovieSearchService/EpisodeSearchService: searches a library item and grabs the best approved releases.
public sealed class AutoSearch(ModularrDbContext db, ReleaseSearch search, DownloadService downloads)
{
    // A movie with a download in progress is not searched again.
    public async Task<IReadOnlyList<TrackedDownload>> SearchMovieAsync(int movieId, CancellationToken cancellationToken)
    {
        int tmdbId = await db.Movies.Where(m => m.Id == movieId).Select(m => (int?)m.TmdbId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Movie {movieId} is not in the library.");
        if (await db.TrackedDownloads.Where(TrackedDownload.InProgress).AnyAsync(d => d.MovieId == movieId, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        return await GrabBestAsync(new ReleaseSearchRequest { Type = MediaType.Movie, TmdbId = tmdbId }, seriesId: null, movieId, wanted: null, cancellationToken).ConfigureAwait(false);
    }

    // The aired, monitored episodes without a file or a running download: a season search when the whole aired season is missing,
    // otherwise one search per episode.
    // shortcut: anime releases numbered only absolutely are rejected by episode searches (ReleaseDecisions wants an episode
    // number), search anime by absolute number when it matters.
    public async Task<IReadOnlyList<TrackedDownload>> SearchSeriesAsync(int seriesId, int? season, DateOnly today, CancellationToken cancellationToken)
    {
        Series series = await db.Series.AsNoTracking().Include(s => s.Episodes).FirstOrDefaultAsync(s => s.Id == seriesId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Series {seriesId} is not in the library.");
        if (!series.Monitored)
        {
            return [];
        }

        List<IReadOnlyList<int>> running = await db.TrackedDownloads.Where(TrackedDownload.InProgress).Where(d => d.SeriesId == seriesId)
            .Select(d => d.EpisodeIds).ToListAsync(cancellationToken).ConfigureAwait(false);

        List<TrackedDownload> grabbed = [];
        foreach ((ReleaseSearchRequest request, IReadOnlySet<(int, int)> wanted) in Plan(series, running.SelectMany(ids => ids).ToHashSet(), season, today))
        {
            grabbed.AddRange(await GrabBestAsync(request, series.Id, movieId: null, wanted, cancellationToken).ConfigureAwait(false));
        }

        return grabbed;
    }

    // The searches for a series' missing episodes, each with the season's missing episodes it may grab.
    public static IReadOnlyList<(ReleaseSearchRequest Request, IReadOnlySet<(int Season, int Episode)> Wanted)> Plan(
        Series series, IReadOnlySet<int> downloading, int? season, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(downloading);
        HashSet<int> monitoredSeasons = [.. series.Seasons.Where(s => s.Monitored).Select(s => s.Number)];
        Episode[] aired = [.. series.Episodes.Where(e => e.AirDate <= today && (season is null || e.SeasonNumber == season))];
        Episode[] missing = [.. aired.Where(e => e.Monitored && monitoredSeasons.Contains(e.SeasonNumber) && e.MediaFileId is null && !downloading.Contains(e.Id))];

        return
        [
            .. missing.GroupBy(e => e.SeasonNumber).SelectMany(seasonEpisodes =>
            {
                IReadOnlySet<(int, int)> wanted = seasonEpisodes.Select(e => (e.SeasonNumber, e.EpisodeNumber)).ToHashSet();
                ReleaseSearchRequest[] requests = seasonEpisodes.Count() == aired.Count(e => e.SeasonNumber == seasonEpisodes.Key)
                    ? [ReleaseSearchRequest.ForSeason(series.TvdbId, seasonEpisodes.Key)]
                    : [.. seasonEpisodes.Select(e => ReleaseSearchRequest.ForEpisode(series.TvdbId, e))];
                return requests.Select(request => (request, wanted));
            }),
        ];
    }

    // Approved releases, the ones covering the most episodes first then best first: one for a movie; for a series each release that
    // covers a wanted episode (any, when wanted is null) and none already grabbed, so a season pack wins over its episodes and two
    // packs never overlap.
    public async Task<IReadOnlyList<TrackedDownload>> GrabBestAsync(
        ReleaseSearchRequest request, int? seriesId, int? movieId, IReadOnlySet<(int Season, int Episode)>? wanted, CancellationToken cancellationToken)
    {
        ReleaseSearchResult result = await search.SearchAsync(request, cancellationToken).ConfigureAwait(false);
        List<TrackedDownload> grabbed = [];
        HashSet<(int, int)> covered = [];
        foreach (DownloadDecision decision in result.Decisions.Where(d => d.Approved).OrderByDescending(d => d.Candidate.Episodes.Count))
        {
            (int, int)[] episodes = [.. decision.Candidate.Episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber))];
            if (seriesId is not null && (episodes.Any(covered.Contains) || (wanted is not null && !episodes.Any(wanted.Contains))))
            {
                continue;
            }

            TrackedDownload next = await downloads.GrabAsync(
                new GrabRequest { Release = decision.Candidate.Release, SeriesId = seriesId, MovieId = movieId }, cancellationToken).ConfigureAwait(false);

            // A failed release again under another title (no infohash from the indexer, so the blocklist missed it): try the next one.
            if (next.State == TrackedDownloadState.Failed)
            {
                continue;
            }

            grabbed.Add(next);
            covered.UnionWith(episodes);
            if (movieId is not null)
            {
                break;
            }
        }

        return grabbed;
    }
}
