using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Persistence;

namespace Modularr.Core.Search;

// Upstream's RSS sync job, done by searching: every SearchIntervalHours, queues searches for up to SearchTitlesPerCycle titles with a
// missing or upgradable item, going on each time from the title after the last one queued so every title gets its turn.
// shortcut: the turn is kept in memory and starts over on restart, store it if large libraries starve their last titles.
public sealed class ScheduledSearch(IServiceScopeFactory scopes, SearchRunner searches, IOptionsMonitor<ModularrOptions> options, ILogger<ScheduledSearch> logger) : BackgroundService
{
    private SearchJob? _last;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // After the start's metadata refresh, so new episodes are searched too.
        await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            int hours = options.CurrentValue.Automation.SearchIntervalHours;
            if (hours > 0)
            {
                try
                {
                    await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                    List<SearchJob> wanted = await WantedAsync(scope.ServiceProvider.GetRequiredService<ModularrDbContext>(), DateOnly.FromDateTime(DateTime.UtcNow), stoppingToken).ConfigureAwait(false);
                    IReadOnlyList<SearchJob> picked = Pick(wanted, _last, options.CurrentValue.Automation.SearchTitlesPerCycle);
                    foreach (SearchJob job in picked)
                    {
                        searches.Enqueue(job);
                    }

                    _last = picked.Count > 0 ? picked[^1] : _last;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.JobFailed("Scheduled search", ex);
                    // An exception escaping ExecuteAsync stops the host; a busy database is searched on the next round.
                }
            }

            // Turned off: the setting is read again in an hour.
            await Task.Delay(TimeSpan.FromHours(hours > 0 ? hours : 1), stoppingToken).ConfigureAwait(false);
        }
    }

    // The monitored titles AutoSearch would search: a series with an aired, monitored episode missing or below the cutoff (not
    // downloading), a released movie without a file or below the cutoff (not downloading). Series by id, then movies by id.
    public static async Task<List<SearchJob>> WantedAsync(ModularrDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        List<TrackedDownload> running = await db.TrackedDownloads.AsNoTracking().Where(TrackedDownload.InProgress).ToListAsync(cancellationToken).ConfigureAwait(false);
        HashSet<int> downloadingEpisodes = [.. running.SelectMany(d => d.EpisodeIds)];
        HashSet<int?> downloadingMovies = [.. running.Select(d => d.MovieId)];
        List<MediaFile> files = await db.MediaFiles.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        ILookup<int?, MediaFile> seriesFiles = files.ToLookup(f => f.SeriesId);
        ILookup<int?, MediaFile> movieFiles = files.ToLookup(f => f.MovieId);

        List<SearchJob> wanted = [];
        foreach (Series series in await db.Series.AsNoTracking().Include(s => s.Episodes).Include(s => s.QualityProfile)
            .Where(s => s.Monitored).OrderBy(s => s.Id).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            HashSet<int> belowCutoff = [.. seriesFiles[series.Id].Where(f => PresentFiles.IsWanted(f, series.QualityProfile, upgrade: true)).Select(f => f.Id)];
            if (AutoSearch.Plan(series, downloadingEpisodes, season: null, today, belowCutoff).Count > 0)
            {
                wanted.Add(new SearchJob(series.Id, MovieId: null));
            }
        }

        foreach (Movie movie in await db.Movies.AsNoTracking().Include(m => m.QualityProfile)
            .Where(m => m.Monitored).OrderBy(m => m.Id).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (IsReleased(movie, today) && !downloadingMovies.Contains(movie.Id)
                && PresentFiles.IsWanted(movieFiles[movie.Id].FirstOrDefault(), movie.QualityProfile, upgrade: true))
            {
                wanted.Add(new SearchJob(SeriesId: null, movie.Id));
            }
        }

        return wanted;
    }

    // Out on any release day (cinema, digital or physical); with none known, from the year after its year.
    public static bool IsReleased(Movie movie, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(movie);
        return new[] { movie.InCinemas, movie.DigitalRelease, movie.PhysicalRelease }.Min() is DateOnly released
            ? released <= today
            : movie.Year > 0 && movie.Year < today.Year;
    }

    // Up to cap titles from wanted (in WantedAsync's order), starting after the last one taken and wrapping around.
    public static IReadOnlyList<SearchJob> Pick(IReadOnlyList<SearchJob> wanted, SearchJob? after, int cap)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        int start = after is null ? 0 : wanted.Count(job => Order(job).CompareTo(Order(after)) <= 0);
        return [.. wanted.Skip(start).Concat(wanted.Take(start)).Take(cap)];
    }

    private static (int, int) Order(SearchJob job) => job.MovieId is int movieId ? (1, movieId) : (0, job.SeriesId ?? 0);
}
