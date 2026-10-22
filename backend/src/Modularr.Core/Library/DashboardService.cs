using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Persistence;
using Modularr.Core.Subtitles;

namespace Modularr.Core.Library;

// Episodes count regular seasons only. Missing: monitored episodes aired without a file, and monitored movies without one.
public sealed record DashboardCounts(int Movies, int Series, int Anime, int Episodes, int EpisodesWithFile, int Missing);

// Used, free and total are summed over the distinct volumes holding the library and downloads folders; LibraryBytes is the
// imported files alone.
public sealed record DashboardStorage(long UsedBytes, long FreeBytes, long TotalBytes, long LibraryBytes);

public sealed record DashboardSubtitles(int Covered, int Total);

public sealed record AiringEpisode(
    int Id, int SeriesId, string SeriesTitle, string? Poster, int SeasonNumber, int EpisodeNumber, string? Title, DateOnly AirDate, bool HasFile, bool Monitored);

public sealed record DashboardSummary(
    DashboardCounts Counts, DashboardStorage Storage, DashboardSubtitles Subtitles, IReadOnlyList<AiringEpisode> Airing, IReadOnlyList<Activity> Recent);

// The home page in one call. Counts are SQL aggregates; subtitle coverage reads the disk, so it is cached for a few minutes.
public sealed class DashboardService(ModularrDbContext db, SubtitleService subtitles, IOptionsMonitor<ModularrOptions> options, IMemoryCache cache)
{
    public const int RecentCount = 10;
    private static readonly TimeSpan CoverageLifetime = TimeSpan.FromMinutes(5);

    // Airing covers the ISO week (Monday to Sunday) holding today.
    public async Task<DashboardSummary> GetAsync(DateOnly today, CancellationToken cancellationToken)
    {
        DateOnly monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        DateOnly nextMonday = monday.AddDays(7);

        int movies = await db.Movies.CountAsync(cancellationToken).ConfigureAwait(false);
        int series = await db.Series.CountAsync(s => s.SeriesType != SeriesType.Anime, cancellationToken).ConfigureAwait(false);
        int anime = await db.Series.CountAsync(s => s.SeriesType == SeriesType.Anime, cancellationToken).ConfigureAwait(false);
        int episodes = await db.Episodes.CountAsync(e => e.SeasonNumber > 0, cancellationToken).ConfigureAwait(false);
        int episodesWithFile = await db.Episodes.CountAsync(e => e.SeasonNumber > 0 && e.MediaFileId != null, cancellationToken).ConfigureAwait(false);
        int missing = await db.Episodes.CountAsync(e => e.Monitored && e.Series.Monitored && e.MediaFileId == null && e.AirDate <= today, cancellationToken).ConfigureAwait(false)
            + await db.Movies.CountAsync(m => m.Monitored && !db.MediaFiles.Any(f => f.MovieId == m.Id), cancellationToken).ConfigureAwait(false);
        long libraryBytes = await db.MediaFiles.SumAsync(f => f.Size, cancellationToken).ConfigureAwait(false);

        List<AiringEpisode> airing = await db.Episodes.AsNoTracking()
            .Where(e => e.AirDate >= monday && e.AirDate < nextMonday)
            .OrderBy(e => e.AirDate).ThenBy(e => e.Series.SortTitle).ThenBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber)
            .Select(e => new AiringEpisode(
                e.Id, e.SeriesId, e.Series.Title, e.Series.Poster, e.SeasonNumber, e.EpisodeNumber, e.Title, e.AirDate!.Value, e.MediaFileId != null, e.Monitored))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        List<Activity> recent = await db.Activities.AsNoTracking().OrderByDescending(a => a.Id).Take(RecentCount).ToListAsync(cancellationToken).ConfigureAwait(false);

        (int covered, int total) = await cache.GetOrCreateAsync("dashboard:subtitles", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CoverageLifetime;
            entry.Size = 1;
            return subtitles.CoverageAsync(cancellationToken);
        }).ConfigureAwait(false);

        return new DashboardSummary(
            new DashboardCounts(movies, series, anime, episodes, episodesWithFile, missing),
            Storage(libraryBytes),
            new DashboardSubtitles(covered, total),
            airing,
            recent);
    }

    private DashboardStorage Storage(long libraryBytes)
    {
        PathOptions paths = options.CurrentValue.Paths;
        // No device id in .NET: two folders on one volume report the same sizes, so the sizes tell volumes apart.
        (long Total, long Free)[] volumes =
        [
            .. new[] { paths.Movies, paths.Series, paths.Anime, paths.Downloads }
                .Select(VolumeOf).OfType<(long, long)>().Distinct(),
        ];
        long total = volumes.Sum(v => v.Total);
        long free = volumes.Sum(v => v.Free);
        return new DashboardStorage(total - free, free, total, libraryBytes);
    }

    // Statistics of the volume holding the folder (its nearest existing parent until it is created); null when unreadable.
    private static (long Total, long Free)? VolumeOf(string folder)
    {
        try
        {
            DirectoryInfo? existing = new(Path.GetFullPath(folder));
            while (existing is not null && !existing.Exists)
            {
                existing = existing.Parent;
            }

            if (existing is null)
            {
                return null;
            }

            DriveInfo drive = new(OperatingSystem.IsWindows() ? existing.Root.FullName : existing.FullName);
            return (drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
