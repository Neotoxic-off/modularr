using Microsoft.EntityFrameworkCore;
using Modularr.Core.Activities;
using Modularr.Core.Decisions;
using Modularr.Core.Downloads;
using Modularr.Core.Persistence;

namespace Modularr.Core.Search;

// What one indexer search found and what came of it.
public sealed record GrabResult(IReadOnlyList<TrackedDownload> Grabbed, int Releases, int Approved);

// Upstream MovieSearchService/EpisodeSearchService: searches a library item and grabs the best approved releases.
public sealed class AutoSearch(ModularrDbContext db, ReleaseSearch search, DownloadService downloads, ActivityLog activities)
{
    // A movie with a download in progress, or with a file that meets its profile's cutoff, is not searched again.
    public async Task<IReadOnlyList<TrackedDownload>> SearchMovieAsync(int movieId, CancellationToken cancellationToken)
    {
        Movie movie = await db.Movies.AsNoTracking().Include(m => m.QualityProfile).FirstOrDefaultAsync(m => m.Id == movieId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Movie {movieId} is not in the library.");
        if (await db.TrackedDownloads.Where(TrackedDownload.InProgress).AnyAsync(d => d.MovieId == movieId, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        if (!PresentFiles.IsWanted(
            await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(f => f.MovieId == movieId, cancellationToken).ConfigureAwait(false), movie.QualityProfile, upgrade: true))
        {
            await activities.RecordAsync(
                new Activity { Kind = ActivityKind.SearchCompleted, Level = ActivityLevel.Info, Title = movie.Title, Message = "Its file meets the quality cutoff", MovieId = movieId },
                cancellationToken).ConfigureAwait(false);
            return [];
        }

        return await RunAsync(
            new Activity { Title = movie.Title, Message = "Searching for the movie", MovieId = movieId },
            [(new ReleaseSearchRequest { Type = MediaType.Movie, TmdbId = movie.TmdbId }, null)],
            cancellationToken).ConfigureAwait(false);
    }

    // The aired, monitored episodes without a file (or with one below the profile's cutoff) or a running download: a season search
    // when the whole aired season is missing, otherwise one search per episode.
    // shortcut: anime releases numbered only absolutely are rejected by episode searches (ReleaseDecisions wants an episode
    // number), search anime by absolute number when it matters.
    public async Task<IReadOnlyList<TrackedDownload>> SearchSeriesAsync(int seriesId, int? season, DateOnly today, CancellationToken cancellationToken)
    {
        Series series = await db.Series.AsNoTracking().Include(s => s.Episodes).Include(s => s.QualityProfile)
            .FirstOrDefaultAsync(s => s.Id == seriesId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Series {seriesId} is not in the library.");
        if (!series.Monitored)
        {
            return [];
        }

        List<IReadOnlyList<int>> running = await db.TrackedDownloads.Where(TrackedDownload.InProgress).Where(d => d.SeriesId == seriesId)
            .Select(d => d.EpisodeIds).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<MediaFile> files = await db.MediaFiles.AsNoTracking().Where(f => f.SeriesId == seriesId).ToListAsync(cancellationToken).ConfigureAwait(false);
        HashSet<int> belowCutoff = [.. files.Where(f => PresentFiles.IsWanted(f, series.QualityProfile, upgrade: true)).Select(f => f.Id)];

        IReadOnlyList<(ReleaseSearchRequest Request, IReadOnlySet<(int Season, int Episode)> Wanted)> plan =
            Plan(series, running.SelectMany(ids => ids).ToHashSet(), season, today, belowCutoff);
        if (plan.Count == 0)
        {
            await activities.RecordAsync(
                new Activity { Kind = ActivityKind.SearchCompleted, Level = ActivityLevel.Info, Title = series.Title, Message = "No aired episode is missing", SeriesId = seriesId },
                cancellationToken).ConfigureAwait(false);
            return [];
        }

        int missing = plan.SelectMany(p => p.Wanted).Distinct().Count();
        return await RunAsync(
            new Activity { Title = series.Title, Message = $"Searching for {missing} missing episode(s)", SeriesId = seriesId },
            [.. plan.Select(p => (p.Request, (IReadOnlySet<(int, int)>?)p.Wanted))],
            cancellationToken).ConfigureAwait(false);
    }

    // Records the search's start and its outcome (grabbed, nothing grabbed or failed) around the indexer searches.
    private async Task<IReadOnlyList<TrackedDownload>> RunAsync(
        Activity started, IReadOnlyList<(ReleaseSearchRequest Request, IReadOnlySet<(int, int)>? Wanted)> plan, CancellationToken cancellationToken)
    {
        started.Kind = ActivityKind.SearchStarted;
        started.Level = ActivityLevel.Info;
        await activities.RecordAsync(started, cancellationToken).ConfigureAwait(false);
        Activity Outcome(ActivityKind kind, ActivityLevel level, string message) =>
            new() { Kind = kind, Level = level, Title = started.Title, Message = message, SeriesId = started.SeriesId, MovieId = started.MovieId };

        List<TrackedDownload> grabbed = [];
        int releases = 0;
        int approved = 0;
        try
        {
            foreach ((ReleaseSearchRequest request, IReadOnlySet<(int, int)>? wanted) in plan)
            {
                GrabResult result = await GrabBestAsync(request, started.SeriesId, started.MovieId, wanted, cancellationToken).ConfigureAwait(false);
                grabbed.AddRange(result.Grabbed);
                releases += result.Releases;
                approved += result.Approved;
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await activities.RecordAsync(Outcome(ActivityKind.SearchFailed, ActivityLevel.Error, ex.Message), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A busy database fails this save too; the search's own error is the one to report.
            }

            throw;
        }

        string found = $"{releases} release(s) found, {approved} approved";
        await activities.RecordAsync(
            grabbed.Count > 0
                ? Outcome(ActivityKind.SearchCompleted, ActivityLevel.Success, $"{found}, {grabbed.Count} grabbed")
                : Outcome(ActivityKind.NothingFound, ActivityLevel.Warning, $"{found}, nothing grabbed"),
            cancellationToken).ConfigureAwait(false);
        return grabbed;
    }

    // The searches for a series' missing episodes, each with the season's missing episodes it may grab. An episode whose file is
    // below the cutoff (belowCutoff holds MediaFile ids) counts as missing.
    public static IReadOnlyList<(ReleaseSearchRequest Request, IReadOnlySet<(int Season, int Episode)> Wanted)> Plan(
        Series series, IReadOnlySet<int> downloading, int? season, DateOnly today, IReadOnlySet<int>? belowCutoff = null)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(downloading);
        HashSet<int> monitoredSeasons = [.. series.Seasons.Where(s => s.Monitored).Select(s => s.Number)];
        bool Missing(Episode e) => e.Monitored && monitoredSeasons.Contains(e.SeasonNumber) && !downloading.Contains(e.Id)
            && (e.MediaFileId is not int fileId || belowCutoff?.Contains(fileId) == true);
        Episode[] aired = [.. series.Episodes.Where(e => e.AirDate <= today && (season is null || e.SeasonNumber == season))];
        Episode[] missing = [.. aired.Where(Missing)];

        return
        [
            .. missing.GroupBy(e => e.SeasonNumber).SelectMany(seasonEpisodes =>
            {
                // Not yet aired ones too: a pack may carry them, and it is refused when it holds any episode not wanted.
                IReadOnlySet<(int, int)> wanted = series.Episodes.Where(e => e.SeasonNumber == seasonEpisodes.Key && Missing(e))
                    .Select(e => (e.SeasonNumber, e.EpisodeNumber)).ToHashSet();
                ReleaseSearchRequest[] requests = seasonEpisodes.Count() == aired.Count(e => e.SeasonNumber == seasonEpisodes.Key)
                    ? [ReleaseSearchRequest.ForSeason(series.TvdbId, seasonEpisodes.Key)]
                    : [.. seasonEpisodes.Select(e => ReleaseSearchRequest.ForEpisode(series.TvdbId, e))];
                return requests.Select(request => (request, wanted));
            }),
        ];
    }

    // Approved releases, the ones covering the most episodes first then best first: one for a movie; for a series each release whose
    // episodes are all wanted (any, when wanted is null) and none already grabbed, so a season pack is taken only when its whole season
    // is wanted, wins over its episodes then, and two packs never overlap.
    public async Task<GrabResult> GrabBestAsync(
        ReleaseSearchRequest request, int? seriesId, int? movieId, IReadOnlySet<(int Season, int Episode)>? wanted, CancellationToken cancellationToken)
    {
        ReleaseSearchResult result = await search.SearchAsync(request, cancellationToken).ConfigureAwait(false);
        List<TrackedDownload> grabbed = [];
        HashSet<(int, int)> covered = [];
        foreach (DownloadDecision decision in result.Decisions.Where(d => d.Approved).OrderByDescending(d => d.Candidate.Episodes.Count))
        {
            (int, int)[] episodes = [.. decision.Candidate.Episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber))];
            if (seriesId is not null && (episodes.Any(covered.Contains) || (wanted is not null && !episodes.All(wanted.Contains))))
            {
                continue;
            }

            TrackedDownload next;
            try
            {
                next = await downloads.GrabAsync(
                    new GrabRequest { Release = decision.Candidate.Release, SeriesId = seriesId, MovieId = movieId, Upgrade = true }, cancellationToken).ConfigureAwait(false);
            }
            catch (AlreadyPresentException)
            {
                // A re-grab (wanted is null) for episodes imported meanwhile.
                continue;
            }

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

        return new GrabResult(grabbed, result.Decisions.Count, result.Decisions.Count(d => d.Approved));
    }
}
