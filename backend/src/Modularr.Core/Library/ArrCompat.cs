using System.Globalization;
using Modularr.Core.Configuration;
using Modularr.Core.Downloads;
using Modularr.Core.Downloads.Torrents;
using Modularr.Core.Persistence;

namespace Modularr.Core.Library;

// The Sonarr/Radarr v3 JSON shapes that request apps (Overseerr, Jellyseerr, Ombi) read; the web serializer camelCases them.
public sealed record ArrImage(string CoverType, string Url, string RemoteUrl);

public sealed record ArrStatistics(int EpisodeFileCount, int EpisodeCount, int TotalEpisodeCount, decimal PercentOfEpisodes, int? SeasonCount = null);

public sealed record ArrSeason(int SeasonNumber, bool Monitored, ArrStatistics? Statistics = null);

public sealed record ArrSeries(
    int Id, string Title, string SortTitle, int TvdbId, int Year, string? Overview, bool Monitored, int QualityProfileId, int LanguageProfileId,
    string SeriesType, bool SeasonFolder, string Path, string RootFolderPath, string TitleSlug, DateTime Added,
    IReadOnlyList<ArrSeason> Seasons, ArrStatistics Statistics, IReadOnlyList<ArrImage> Images, IReadOnlyList<int> Tags);

public sealed record ArrMovie(
    int Id, string Title, int TmdbId, string? ImdbId, int Year, string? Overview, bool Monitored, bool HasFile, int QualityProfileId,
    string Path, string RootFolderPath, string TitleSlug, string MinimumAvailability, IReadOnlyList<ArrImage> Images, IReadOnlyList<int> Tags);

public sealed record ArrRootFolder(int Id, string Path, long FreeSpace, bool Accessible, IReadOnlyList<string> UnmappedFolders);

public sealed record ArrQueueMovie(int TmdbId, string Title);

public sealed record ArrQueueSeries(int TvdbId, string Title);

public sealed record ArrQueueItem(
    int Id, string Title, long Size, long Sizeleft, string Status, string TrackedDownloadState, string? Timeleft, DateTime? EstimatedCompletionTime,
    string DownloadId, int? MovieId, int? SeriesId, int? EpisodeId, ArrQueueMovie? Movie, ArrQueueSeries? Series);

public sealed record ArrQueuePage(int Page, int PageSize, int TotalRecords, IReadOnlyList<ArrQueueItem> Records);

public static class ArrCompat
{
    // A single language profile: languages follow the version policy, not a per-series profile.
    public const int LanguageProfileId = 1;

    // Id 0 is a title not in the library yet (a lookup result), as Sonarr returns it.
    public static ArrSeries ToSonarr(Series series, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArrSeason[] seasons = [.. series.Seasons.OrderBy(s => s.Number)
            .Select(s => new ArrSeason(s.Number, s.Monitored, Statistics(series.Episodes.Where(e => e.SeasonNumber == s.Number), today)))];
        return new ArrSeries(
            series.Id, series.Title, series.SortTitle, series.TvdbId, series.Year, series.Overview, series.Monitored, series.QualityProfileId, LanguageProfileId,
            series.SeriesType.ToString().ToLowerInvariant(), SeasonFolder: true, series.Path, RootOf(series.Path),
            series.TvdbId.ToString(CultureInfo.InvariantCulture), series.Added, seasons,
            Statistics(series.Episodes, today) with { SeasonCount = seasons.Count(s => s.SeasonNumber > 0) },
            Images(series.Poster, series.Backdrop), []);
    }

    public static ArrMovie ToRadarr(Movie movie, bool hasFile)
    {
        ArgumentNullException.ThrowIfNull(movie);
        return new ArrMovie(
            movie.Id, movie.Title, movie.TmdbId, movie.ImdbId, movie.Year, movie.Overview, movie.Monitored, hasFile, movie.QualityProfileId,
            movie.Path, RootOf(movie.Path), movie.TmdbId.ToString(CultureInfo.InvariantCulture), "released", Images(movie.Poster, movie.Backdrop), []);
    }

    // Sonarr's and Radarr's queue: one record per download (episodeId only when it is for a single episode), newest first as listed.
    // movies / series are given only when the client asked to include them.
    public static ArrQueuePage Queue(
        IReadOnlyList<DownloadItem> downloads, int page, int pageSize, DateTime now, IReadOnlyDictionary<int, Movie>? movies = null, IReadOnlyDictionary<int, Series>? series = null)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 1000);
        ArrQueueItem[] records = [.. downloads.Skip((page - 1) * pageSize).Take(pageSize).Select(item =>
        {
            TrackedDownload download = item.Download;
            TorrentStatus? torrent = item.Torrent;
            TimeSpan? left = torrent?.RemainingTime;
            string state = download.State.ToString();
            return new ArrQueueItem(
                download.Id, download.ReleaseTitle, torrent?.TotalSize ?? 0, torrent?.RemainingSize ?? 0,
                torrent is null ? "queued" : JsonName(torrent.Status.ToString()), JsonName(state),
                left is TimeSpan t ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}") : null,
                left is TimeSpan remaining ? now + remaining : null,
                download.InfoHash.ToUpperInvariant(), download.MovieId, download.SeriesId, download.EpisodeIds.Count == 1 ? download.EpisodeIds[0] : null,
                download.MovieId is int movieId && movies?.GetValueOrDefault(movieId) is Movie movie ? new ArrQueueMovie(movie.TmdbId, movie.Title) : null,
                download.SeriesId is int seriesId && series?.GetValueOrDefault(seriesId) is Series show ? new ArrQueueSeries(show.TvdbId, show.Title) : null);
        })];
        return new ArrQueuePage(page, pageSize, downloads.Count, records);
    }

    private static string JsonName(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    // Ids are fixed by kind, so a client keeps its choice when a path changes.
    public static IReadOnlyList<ArrRootFolder> RootFolders(PathOptions paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return [Root(1, paths.Series), Root(2, paths.Anime), Root(3, paths.Movies)];
    }

    private static ArrRootFolder Root(int id, string path) =>
        DashboardService.VolumeOf(path) is (_, _, long free) ? new(id, path, free, Accessible: true, []) : new(id, path, 0, Accessible: false, []);

    // Sonarr's counts: episodeCount is the monitored episodes aired so far plus any with a file.
    private static ArrStatistics Statistics(IEnumerable<Episode> episodes, DateOnly today)
    {
        Episode[] all = [.. episodes];
        int withFile = all.Count(e => e.MediaFileId is not null);
        int counted = all.Count(e => e.MediaFileId is not null || (e.Monitored && e.AirDate <= today));
        return new ArrStatistics(withFile, counted, all.Length, counted == 0 ? 0 : Math.Round(100m * withFile / counted, 1));
    }

    private static string RootOf(string path) => System.IO.Path.GetDirectoryName(path) ?? path;

    private static ArrImage[] Images(string? poster, string? backdrop) =>
        [.. new[] { ("poster", poster), ("fanart", backdrop) }.Where(i => i.Item2 is not null).Select(i => new ArrImage(i.Item1, i.Item2!, i.Item2!))];
}
