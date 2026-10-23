using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Languages;
using Modularr.Core.Metadata;
using Modularr.Core.Parsing.Movies;
using Modularr.Core.Persistence;
using Modularr.Core.Search;

namespace Modularr.Core.Library;

public sealed record EpisodeKey(int Season, int Episode);

// Sonarr's add-series monitor choices that matter here. Specials are monitored only when Custom names season 0.
public enum SeriesMonitor
{
    All,
    Future,
    None,
    Latest,
    First,

    // The seasons given with it.
    Custom,
}

// PATCH body: an absent field is left unchanged; Version or Subtitles present as null clears the item's override.
public sealed record ItemUpdate
{
    public int? QualityProfileId { get; init; }
    public bool? Monitored { get; init; }

    public ReleaseVersion? Version
    {
        get;
        init
        {
            field = value;
            VersionSet = true;
        }
    }

    public IReadOnlyList<Language>? Subtitles
    {
        get;
        init
        {
            field = value;
            SubtitlesSet = true;
        }
    }

    // The JSON reader calls the setters only for a field present in the body.
    [JsonIgnore]
    public bool VersionSet { get; private init; }

    [JsonIgnore]
    public bool SubtitlesSet { get; private init; }
}

// Adds series and movies from the metadata sources; the one place metadata records become library entities.
public sealed class LibraryService(
    ModularrDbContext db,
    SeriesMetadataClient seriesMetadata,
    MovieMetadataClient movieMetadata,
    IOptionsMonitor<ModularrOptions> options,
    SearchRunner searches,
    ActivityLog activities)
{
    // Windows' set, so folder names are the same on every OS.
    private static readonly char[] InvalidNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    // Split: one joined query would repeat every series row (overview, genres, seasons) once per episode.
    public async Task<IReadOnlyList<Series>> ListSeriesAsync(CancellationToken cancellationToken) =>
        await db.Series.AsNoTracking().Include(s => s.Episodes).AsSplitQuery().OrderBy(s => s.SortTitle).ToListAsync(cancellationToken).ConfigureAwait(false);

    // With whether each movie has a file, in one query.
    public async Task<IReadOnlyList<(Movie Movie, bool HasFile)>> ListMoviesAsync(CancellationToken cancellationToken) =>
        [.. (await db.Movies.AsNoTracking().OrderBy(m => m.Title)
            .Select(m => new { Movie = m, HasFile = db.MediaFiles.Any(f => f.MovieId == m.Id) })
            .ToListAsync(cancellationToken).ConfigureAwait(false)).Select(m => (m.Movie, m.HasFile))];

    public async Task<Series> GetSeriesAsync(int id, CancellationToken cancellationToken) =>
        await db.Series.AsNoTracking().Include(s => s.Episodes).AsSplitQuery().FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Series {id} is not in the library.");

    public async Task<(Movie Movie, MediaFile? File)> GetMovieAsync(int id, CancellationToken cancellationToken)
    {
        Movie movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Movie {id} is not in the library.");
        return (movie, await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(f => f.MovieId == id, cancellationToken).ConfigureAwait(false));
    }

    // Adding an existing series returns it unchanged. No profile: the category default (series or anime) from MediaOptions.
    // A monitored series is searched in the background unless search is false. Seasons and episodes are read for
    // SeriesMonitor.Custom only: picked episodes, when given, are the only monitored ones in their seasons.
    public async Task<Series> AddSeriesAsync(
        int tvdbId, int? qualityProfileId, ReleaseVersion? version, SeriesMonitor monitor, IReadOnlyCollection<int>? seasons, bool search, CancellationToken cancellationToken,
        IReadOnlyCollection<EpisodeKey>? episodes = null, IReadOnlyList<Language>? subtitles = null)
    {
        MediaOptions.EnsureSubtitleLanguages(subtitles);
        Series series = await db.Series.Include(s => s.Episodes).FirstOrDefaultAsync(s => s.TvdbId == tvdbId, cancellationToken).ConfigureAwait(false)
            ?? await CreateSeriesAsync(tvdbId, qualityProfileId, version, monitor, seasons, episodes, subtitles, cancellationToken).ConfigureAwait(false);
        if (search && series.Monitored)
        {
            searches.Enqueue(new SearchJob(series.Id, MovieId: null));
        }

        return series;
    }

    // Subtitles, when given, override the category's wanted subtitle languages from the start.
    public async Task<Movie> AddMovieAsync(
        int tmdbId, int? qualityProfileId, ReleaseVersion? version, bool search, CancellationToken cancellationToken, IReadOnlyList<Language>? subtitles = null)
    {
        MediaOptions.EnsureSubtitleLanguages(subtitles);
        Movie movie = await db.Movies.FirstOrDefaultAsync(m => m.TmdbId == tmdbId, cancellationToken).ConfigureAwait(false)
            ?? await CreateMovieAsync(tmdbId, qualityProfileId, version, subtitles, cancellationToken).ConfigureAwait(false);
        if (search && movie.Monitored)
        {
            searches.Enqueue(new SearchJob(SeriesId: null, movie.Id));
        }

        return movie;
    }

    private async Task<Series> CreateSeriesAsync(
        int tvdbId, int? qualityProfileId, ReleaseVersion? version, SeriesMonitor monitor, IReadOnlyCollection<int>? seasons,
        IReadOnlyCollection<EpisodeKey>? episodes, IReadOnlyList<Language>? subtitles, CancellationToken cancellationToken)
    {
        SeriesInfo info = await seriesMetadata.GetSeriesAsync(tvdbId, cancellationToken: cancellationToken).ConfigureAwait(false);
        MediaType category = TypeOf(info) == SeriesType.Anime ? MediaType.Anime : MediaType.Series;
        int profileId = qualityProfileId ?? options.CurrentValue.Media.For(category).ProfileId;
        await EnsureProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        Series series = ToSeries(info, profileId, options.CurrentValue.Paths, DateTime.UtcNow, monitor, seasons, episodes);
        series.Path = await OwnFolderAsync(series.Path, $"tvdbid-{tvdbId}", cancellationToken).ConfigureAwait(false);
        series.Version = version;
        series.Subtitles = subtitles;
        db.Series.Add(series);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await activities.RecordAsync(
            new Activity { Kind = ActivityKind.Added, Level = ActivityLevel.Info, Title = series.Title, Message = $"Added to {category.ToString().ToLowerInvariant()}", SeriesId = series.Id },
            cancellationToken).ConfigureAwait(false);
        return series;
    }

    private async Task<Movie> CreateMovieAsync(int tmdbId, int? qualityProfileId, ReleaseVersion? version, IReadOnlyList<Language>? subtitles, CancellationToken cancellationToken)
    {
        int profileId = qualityProfileId ?? options.CurrentValue.Media.Movies.ProfileId;
        await EnsureProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        MovieInfo info = await movieMetadata.GetMovieAsync(tmdbId, cancellationToken).ConfigureAwait(false);
        Movie movie = ToMovie(info, profileId, options.CurrentValue.Paths);
        movie.Path = await OwnFolderAsync(movie.Path, $"tmdbid-{tmdbId}", cancellationToken).ConfigureAwait(false);
        movie.Version = version;
        movie.Subtitles = subtitles;
        db.Movies.Add(movie);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await activities.RecordAsync(
            new Activity { Kind = ActivityKind.Added, Level = ActivityLevel.Info, Title = movie.Title, Message = "Added to movies", MovieId = movie.Id },
            cancellationToken).ConfigureAwait(false);
        return movie;
    }

    // Items added before the MediaPreferences migration have no original language, backdrop, rating or genres, and movies added
    // before CalendarDates no release days: fetch them once. A source that is down or no longer knows an item leaves it for the
    // next start. Returns the number of items filled.
    // shortcut: an item whose source has no original language (or a movie with no release day) is fetched again on every start,
    // store a refreshed flag if libraries grow large.
    public async Task<int> BackfillMetadataAsync(CancellationToken cancellationToken)
    {
        int filled = 0;
        foreach (Movie movie in await db.Movies
            .Where(m => m.OriginalLanguage == Language.Unknown || (m.InCinemas == null && m.DigitalRelease == null && m.PhysicalRelease == null))
            .ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await FetchAsync(() => movieMetadata.GetMovieAsync(movie.TmdbId, cancellationToken), cancellationToken).ConfigureAwait(false) is MovieInfo info)
            {
                ApplyMetadata(movie, info);
                filled++;
            }
        }

        foreach (Series series in await db.Series.Where(s => s.OriginalLanguage == Language.Unknown).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await FetchAsync(() => seriesMetadata.GetSeriesAsync(series.TvdbId, cancellationToken: cancellationToken), cancellationToken).ConfigureAwait(false) is SeriesInfo info)
            {
                ApplyMetadata(series, info);
                filled++;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return filled;
    }

    // Fetches every title's metadata again, one at a time with a pause between them so the sources are not hammered. A title that
    // fails (source down, removed meanwhile) is left for the next refresh. Returns the number of titles refreshed.
    public async Task<int> RefreshAllAsync(TimeSpan pause, CancellationToken cancellationToken)
    {
        List<int> seriesIds = await db.Series.Select(s => s.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<int> movieIds = await db.Movies.Select(m => m.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        int refreshed = 0;
        async Task RefreshOneAsync(Func<Task> refresh)
        {
            try
            {
                await refresh().ConfigureAwait(false);
                refreshed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException)
            {
            }
            finally
            {
                db.ChangeTracker.Clear();
            }

            await Task.Delay(pause, cancellationToken).ConfigureAwait(false);
        }

        foreach (int id in seriesIds)
        {
            await RefreshOneAsync(() => RefreshSeriesAsync(id, cancellationToken)).ConfigureAwait(false);
        }

        foreach (int id in movieIds)
        {
            await RefreshOneAsync(() => RefreshMovieAsync(id, cancellationToken)).ConfigureAwait(false);
        }

        return refreshed;
    }

    private async Task RefreshSeriesAsync(int id, CancellationToken cancellationToken)
    {
        Series series = await db.Series.Include(s => s.Episodes).FirstAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
        SeriesInfo info = await seriesMetadata.GetSeriesAsync(series.TvdbId, cancellationToken: cancellationToken).ConfigureAwait(false);
        ApplyMetadata(series, info);
        int added = MergeEpisodes(series, info);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (added > 0)
        {
            await activities.RecordAsync(
                new Activity { Kind = ActivityKind.Added, Level = ActivityLevel.Info, Title = series.Title, Message = $"{added} new episode(s)", SeriesId = series.Id },
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshMovieAsync(int id, CancellationToken cancellationToken)
    {
        Movie movie = await db.Movies.FirstAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);
        ApplyMetadata(movie, await movieMetadata.GetMovieAsync(movie.TmdbId, cancellationToken).ConfigureAwait(false));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Brings a series' seasons and episodes in line with its source and returns the number of episodes added. A new season is
    // monitored with the series (specials never), a new episode when its season and the series are. An episode gone upstream is
    // removed unless it has a file; none is when the source lists no episode at all (a partial answer, not an emptied show).
    public static int MergeEpisodes(Series series, SeriesInfo info)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(info);
        foreach (SeasonInfo season in info.Seasons.Where(s => series.Seasons.All(known => known.Number != s.SeasonNumber)))
        {
            series.Seasons.Add(new Season { Number = season.SeasonNumber, Monitored = series.Monitored && season.SeasonNumber > 0 });
        }

        HashSet<int> monitoredSeasons = [.. series.Seasons.Where(s => s.Monitored).Select(s => s.Number)];
        Dictionary<(int, int), EpisodeInfo> upstream = info.Episodes.DistinctBy(e => (e.SeasonNumber, e.EpisodeNumber)).ToDictionary(e => (e.SeasonNumber, e.EpisodeNumber));
        if (upstream.Count > 0)
        {
            foreach (Episode gone in series.Episodes.Where(e => e.MediaFileId is null && !upstream.ContainsKey((e.SeasonNumber, e.EpisodeNumber))).ToList())
            {
                series.Episodes.Remove(gone);
            }
        }

        Dictionary<(int, int), Episode> known = series.Episodes.DistinctBy(e => (e.SeasonNumber, e.EpisodeNumber)).ToDictionary(e => (e.SeasonNumber, e.EpisodeNumber));
        int added = 0;
        foreach (EpisodeInfo episode in upstream.Values)
        {
            if (known.TryGetValue((episode.SeasonNumber, episode.EpisodeNumber), out Episode? existing))
            {
                existing.AirDate = episode.AirDate;
                existing.Title = episode.Title;
                existing.AbsoluteEpisodeNumber = episode.AbsoluteEpisodeNumber;
                continue;
            }

            series.Episodes.Add(new Episode
            {
                SeasonNumber = episode.SeasonNumber,
                EpisodeNumber = episode.EpisodeNumber,
                AbsoluteEpisodeNumber = episode.AbsoluteEpisodeNumber,
                AirDate = episode.AirDate,
                Title = episode.Title,
                Monitored = series.Monitored && monitoredSeasons.Contains(episode.SeasonNumber),
            });
            added++;
        }

        return added;
    }

    private static async Task<T?> FetchAsync<T>(Func<Task<T>> fetch, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await fetch().ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    // Monitored applies to the series only, its seasons and episodes keep their own flags.
    public async Task<Series> UpdateSeriesAsync(int id, ItemUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        Series series = await db.Series.Include(s => s.Episodes).FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Series {id} is not in the library.");
        series.QualityProfileId = await ProfileIdAsync(update, series.QualityProfileId, cancellationToken).ConfigureAwait(false);
        series.Monitored = update.Monitored ?? series.Monitored;
        series.Version = update.VersionSet ? update.Version : series.Version;
        series.Subtitles = update.SubtitlesSet ? MediaOptions.EnsureSubtitleLanguages(update.Subtitles) : series.Subtitles;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return series;
    }

    // Monitors or unmonitors a season and every episode in it.
    public async Task<Series> SetSeasonMonitoredAsync(int seriesId, int seasonNumber, bool monitored, CancellationToken cancellationToken)
    {
        Series series = await db.Series.Include(s => s.Episodes).FirstOrDefaultAsync(s => s.Id == seriesId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Series {seriesId} is not in the library.");
        Season season = series.Seasons.FirstOrDefault(s => s.Number == seasonNumber)
            ?? throw new KeyNotFoundException($"{series.Title} has no season {seasonNumber}.");
        season.Monitored = monitored;
        foreach (Episode episode in series.Episodes.Where(e => e.SeasonNumber == seasonNumber))
        {
            episode.Monitored = monitored;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return series;
    }

    // A monitored episode's season is monitored too, as searches skip unmonitored seasons.
    public async Task<IReadOnlyList<Episode>> SetEpisodesMonitoredAsync(IReadOnlyCollection<int> episodeIds, bool monitored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(episodeIds);
        if (episodeIds.Count == 0)
        {
            throw new ArgumentException("No episode given.", nameof(episodeIds));
        }

        List<Episode> episodes = await db.Episodes.Include(e => e.Series).Where(e => episodeIds.Contains(e.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        int[] unknown = [.. episodeIds.Except(episodes.Select(e => e.Id))];
        if (unknown.Length > 0)
        {
            throw new KeyNotFoundException($"Episode {unknown[0]} is not in the library.");
        }

        foreach (Episode episode in episodes)
        {
            episode.Monitored = monitored;
            if (monitored && episode.Series.Seasons.FirstOrDefault(s => s.Number == episode.SeasonNumber) is Season season)
            {
                season.Monitored = true;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return episodes;
    }

    public async Task<Movie> UpdateMovieAsync(int id, ItemUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        Movie movie = await db.Movies.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Movie {id} is not in the library.");
        movie.QualityProfileId = await ProfileIdAsync(update, movie.QualityProfileId, cancellationToken).ConfigureAwait(false);
        movie.Monitored = update.Monitored ?? movie.Monitored;
        movie.Version = update.VersionSet ? update.Version : movie.Version;
        movie.Subtitles = update.SubtitlesSet ? MediaOptions.EnsureSubtitleLanguages(update.Subtitles) : movie.Subtitles;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return movie;
    }

    public async Task DeleteSeriesAsync(int id, bool deleteFiles, CancellationToken cancellationToken)
    {
        Series series = await db.Series.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Series {id} is not in the library.");
        await DeleteFolderAsync(deleteFiles, series.Path, seriesId: id, movieId: null, cancellationToken).ConfigureAwait(false);
        await db.Series.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await activities.RecordAsync(Removed(series.Title, deleteFiles, seriesId: id, movieId: null), cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteMovieAsync(int id, bool deleteFiles, CancellationToken cancellationToken)
    {
        Movie movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Movie {id} is not in the library.");
        await DeleteFolderAsync(deleteFiles, movie.Path, seriesId: null, movieId: id, cancellationToken).ConfigureAwait(false);
        await db.Movies.Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await activities.RecordAsync(Removed(movie.Title, deleteFiles, seriesId: null, movieId: id), cancellationToken).ConfigureAwait(false);
    }

    private static Activity Removed(string title, bool deleteFiles, int? seriesId, int? movieId) => new()
    {
        Kind = ActivityKind.Removed,
        Level = ActivityLevel.Info,
        Title = title,
        Message = deleteFiles ? "Removed from the library with its files" : "Removed from the library",
        SeriesId = seriesId,
        MovieId = movieId,
    };

    // Only a folder strictly inside a library root that holds no configured root (library or downloads), whatever the roots were
    // when the title was added: never a root, an old root, a parent of a root or anything outside the library.
    public static bool IsDeletableFolder(string path, PathOptions paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string folder = LibraryPaths.Normalize(path);
        string[] all = [paths.Series, paths.Movies, paths.Anime, paths.Downloads];
        return LibraryPaths.Roots(paths).Any(root => LibraryPaths.IsInside(folder, root))
            && !all.Any(root => string.Equals(LibraryPaths.Normalize(root), folder, LibraryPaths.Comparison) || LibraryPaths.IsInside(root, folder));
    }

    public static SeriesType TypeOf(SeriesInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return info.IsDaily ? SeriesType.Daily
            : info.IsAnime ? SeriesType.Anime
            : SeriesType.Standard;
    }

    // The monitored seasons: every regular one (All, Future), the last or first one, the given ones (Custom) or none. Future then
    // monitors only the episodes airing on or after the add day (or not dated yet).
    public static Series ToSeries(
        SeriesInfo info, int qualityProfileId, PathOptions paths, DateTime added, SeriesMonitor monitor = SeriesMonitor.All, IReadOnlyCollection<int>? seasons = null,
        IReadOnlyCollection<EpisodeKey>? episodes = null)
    {
        HashSet<EpisodeKey>? picked = monitor == SeriesMonitor.Custom && episodes is not null ? [.. episodes] : null;
        if (picked is not null)
        {
            // A season holding a picked episode is monitored, like ticking one episode on the title page.
            seasons = [.. (seasons ?? []).Concat(picked.Select(e => e.Season)).Distinct()];
        }

        SeriesType type = TypeOf(info);
        DateOnly today = DateOnly.FromDateTime(added);
        int year = info.Year ?? 0;
        int[] regular = [.. info.Seasons.Select(s => s.SeasonNumber).Where(n => n > 0).Order()];
        HashSet<int> monitoredSeasons = monitor switch
        {
            SeriesMonitor.All or SeriesMonitor.Future => [.. regular],
            SeriesMonitor.Latest => [.. regular.TakeLast(1)],
            SeriesMonitor.First => [.. regular.Take(1)],
            SeriesMonitor.Custom => seasons is null ? throw new ArgumentException("Custom monitoring needs the seasons to monitor.", nameof(seasons)) : [.. seasons],
            _ => [],
        };

        Series series = new()
        {
            TvdbId = info.TvdbId,
            Title = info.Title,
            SortTitle = MovieTitleParser.NormalizeTitle(info.Title),
            Year = year,
            Runtime = info.Runtime,
            SeriesType = type,
            Monitored = monitor != SeriesMonitor.None,
            Path = Path.Combine(type == SeriesType.Anime ? paths.Anime : paths.Series, FolderName(info.Title, year)),
            QualityProfileId = qualityProfileId,
            Added = added,
        };
        ApplyMetadata(series, info);

        // Specials (season 0) are not monitored, like Sonarr's default.
        foreach (SeasonInfo season in info.Seasons)
        {
            series.Seasons.Add(new Season { Number = season.SeasonNumber, Monitored = monitoredSeasons.Contains(season.SeasonNumber) });
        }

        foreach (EpisodeInfo episode in info.Episodes.DistinctBy(e => (e.SeasonNumber, e.EpisodeNumber)))
        {
            series.Episodes.Add(new Episode
            {
                SeasonNumber = episode.SeasonNumber,
                EpisodeNumber = episode.EpisodeNumber,
                AbsoluteEpisodeNumber = episode.AbsoluteEpisodeNumber,
                AirDate = episode.AirDate,
                Title = episode.Title,
                Monitored = picked is not null
                    ? picked.Contains(new EpisodeKey(episode.SeasonNumber, episode.EpisodeNumber))
                    : monitoredSeasons.Contains(episode.SeasonNumber)
                        && (monitor != SeriesMonitor.Future || episode.AirDate is not DateOnly aired || aired >= today),
            });
        }

        return series;
    }

    public static Movie ToMovie(MovieInfo info, int qualityProfileId, PathOptions paths)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(paths);
        Movie movie = new()
        {
            TmdbId = info.TmdbId,
            ImdbId = info.ImdbId,
            Title = info.Title,
            Year = info.Year,
            Runtime = info.Runtime,
            Monitored = true,
            Path = Path.Combine(paths.Movies, FolderName(info.Title, info.Year)),
            QualityProfileId = qualityProfileId,
        };
        ApplyMetadata(movie, info);
        return movie;
    }

    // The descriptive metadata, refreshed by BackfillMetadataAsync; title, year and path stay as added.
    private static void ApplyMetadata(Movie movie, MovieInfo info)
    {
        movie.OriginalLanguage = info.OriginalLanguage;
        movie.Overview = info.Overview;
        movie.Poster = info.Images.UrlOf(MediaCoverType.Poster);
        movie.Backdrop = info.Images.UrlOf(MediaCoverType.Fanart);
        movie.Rating = info.Ratings.Rating;
        movie.Genres = info.Genres;
        movie.Certification = info.Certification;
        movie.InCinemas = DayOf(info.InCinemas);
        movie.DigitalRelease = DayOf(info.DigitalRelease);
        movie.PhysicalRelease = DayOf(info.PhysicalRelease);
        movie.CollectionTmdbId = info.CollectionTmdbId;
        movie.CollectionName = info.CollectionTitle;
    }

    private static DateOnly? DayOf(DateTime? time) => time is DateTime t ? DateOnly.FromDateTime(t) : null;

    private static void ApplyMetadata(Series series, SeriesInfo info)
    {
        series.OriginalLanguage = info.OriginalLanguage;
        series.Overview = info.Overview;
        series.Poster = info.Images.UrlOf(MediaCoverType.Poster);
        series.Backdrop = info.Images.UrlOf(MediaCoverType.Fanart);
        series.Rating = info.Rating;
        series.Genres = info.Genres;
        series.Certification = info.Certification;
    }

    // "Title (Year)" like the Sonarr/Radarr default folder format, invalid characters dropped.
    public static string FolderName(string title, int year)
    {
        string name = year > 0 ? $"{title} ({year})" : title;
        return string.Concat(name.Split(InvalidNameChars)).Trim().TrimEnd('.');
    }

    // Files go before the rows, so a failed delete leaves the item in the library to retry. A folder another title also uses is
    // never deleted: its files are that title's too.
    private async Task DeleteFolderAsync(bool deleteFiles, string path, int? seriesId, int? movieId, CancellationToken cancellationToken)
    {
        if (!deleteFiles || !Directory.Exists(path))
        {
            return;
        }

        if (!IsDeletableFolder(path, options.CurrentValue.Paths))
        {
            throw new ArgumentException($"Refusing to delete '{path}': it is not a title folder inside a library root.", nameof(path));
        }

        List<string> others =
        [
            .. await db.Series.Where(s => s.Id != seriesId).Select(s => s.Path).ToListAsync(cancellationToken).ConfigureAwait(false),
            .. await db.Movies.Where(m => m.Id != movieId).Select(m => m.Path).ToListAsync(cancellationToken).ConfigureAwait(false),
        ];
        if (others.Exists(other => LibraryPaths.Overlaps(other, path)))
        {
            throw new ArgumentException($"Refusing to delete '{path}': another title uses this folder. Remove it without its files.", nameof(path));
        }

        Directory.Delete(path, recursive: true);
    }

    // "Title (Year) [tvdbid-N]": Jellyfin (and Plex, Emby) match the title by the id in the folder name instead of guessing from
    // the title, and the id keeps two titles from ever sharing a folder (a delete with files would take both).
    private async Task<string> OwnFolderAsync(string path, string tag, CancellationToken cancellationToken)
    {
        PathOptions paths = options.CurrentValue.Paths;
        bool isRoot = LibraryPaths.Roots(paths).Any(root => string.Equals(root, LibraryPaths.Normalize(path), LibraryPaths.Comparison));
        string candidate = isRoot ? Path.Combine(path, $"[{tag}]") : Path.Combine(Path.GetDirectoryName(LibraryPaths.Normalize(path))!, $"{Path.GetFileName(LibraryPaths.Normalize(path))} [{tag}]");
        List<string> owned =
        [
            .. await db.Series.Select(s => s.Path).ToListAsync(cancellationToken).ConfigureAwait(false),
            .. await db.Movies.Select(m => m.Path).ToListAsync(cancellationToken).ConfigureAwait(false),
        ];
        return owned.Exists(o => LibraryPaths.Overlaps(o, candidate))
            ? throw new ArgumentException($"'{candidate}' is already used by another title in the library.", nameof(path))
            : candidate;
    }

    private async Task<int> ProfileIdAsync(ItemUpdate update, int current, CancellationToken cancellationToken)
    {
        if (update.QualityProfileId is not int profileId)
        {
            return current;
        }

        await EnsureProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        return profileId;
    }

    private async Task EnsureProfileAsync(int qualityProfileId, CancellationToken cancellationToken)
    {
        if (!await db.QualityProfiles.AnyAsync(p => p.Id == qualityProfileId, cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException($"Quality profile {qualityProfileId} does not exist.", nameof(qualityProfileId));
        }
    }
}
