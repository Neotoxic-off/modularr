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

// Sonarr's add-series monitor choices that matter here. Specials are never monitored.
public enum SeriesMonitor
{
    All,
    Future,
    None,
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

    public async Task<IReadOnlyList<Series>> ListSeriesAsync(CancellationToken cancellationToken) =>
        await db.Series.AsNoTracking().Include(s => s.Episodes).OrderBy(s => s.SortTitle).ToListAsync(cancellationToken).ConfigureAwait(false);

    // With whether each movie has a file, in one query.
    public async Task<IReadOnlyList<(Movie Movie, bool HasFile)>> ListMoviesAsync(CancellationToken cancellationToken) =>
        [.. (await db.Movies.AsNoTracking().OrderBy(m => m.Title)
            .Select(m => new { Movie = m, HasFile = db.MediaFiles.Any(f => f.MovieId == m.Id) })
            .ToListAsync(cancellationToken).ConfigureAwait(false)).Select(m => (m.Movie, m.HasFile))];

    public async Task<Series> GetSeriesAsync(int id, CancellationToken cancellationToken) =>
        await db.Series.AsNoTracking().Include(s => s.Episodes).FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Series {id} is not in the library.");

    public async Task<(Movie Movie, bool HasFile)> GetMovieAsync(int id, CancellationToken cancellationToken)
    {
        Movie movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Movie {id} is not in the library.");
        return (movie, await db.MediaFiles.AnyAsync(f => f.MovieId == id, cancellationToken).ConfigureAwait(false));
    }

    // Adding an existing series returns it unchanged. No profile: the category default (series or anime) from MediaOptions.
    // A monitored series is searched in the background unless search is false.
    public async Task<Series> AddSeriesAsync(int tvdbId, int? qualityProfileId, ReleaseVersion? version, SeriesMonitor monitor, bool search, CancellationToken cancellationToken)
    {
        Series series = await db.Series.Include(s => s.Episodes).FirstOrDefaultAsync(s => s.TvdbId == tvdbId, cancellationToken).ConfigureAwait(false)
            ?? await CreateSeriesAsync(tvdbId, qualityProfileId, version, monitor, cancellationToken).ConfigureAwait(false);
        if (search && series.Monitored)
        {
            searches.Enqueue(new SearchJob(series.Id, MovieId: null));
        }

        return series;
    }

    public async Task<Movie> AddMovieAsync(int tmdbId, int? qualityProfileId, ReleaseVersion? version, bool search, CancellationToken cancellationToken)
    {
        Movie movie = await db.Movies.FirstOrDefaultAsync(m => m.TmdbId == tmdbId, cancellationToken).ConfigureAwait(false)
            ?? await CreateMovieAsync(tmdbId, qualityProfileId, version, cancellationToken).ConfigureAwait(false);
        if (search && movie.Monitored)
        {
            searches.Enqueue(new SearchJob(SeriesId: null, movie.Id));
        }

        return movie;
    }

    private async Task<Series> CreateSeriesAsync(int tvdbId, int? qualityProfileId, ReleaseVersion? version, SeriesMonitor monitor, CancellationToken cancellationToken)
    {
        SeriesInfo info = await seriesMetadata.GetSeriesAsync(tvdbId, cancellationToken: cancellationToken).ConfigureAwait(false);
        MediaType category = TypeOf(info) == SeriesType.Anime ? MediaType.Anime : MediaType.Series;
        int profileId = qualityProfileId ?? options.CurrentValue.Media.For(category).ProfileId;
        await EnsureProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        Series series = ToSeries(info, profileId, options.CurrentValue.Paths, DateTime.UtcNow, monitor);
        series.Version = version;
        db.Series.Add(series);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await activities.RecordAsync(
            new Activity { Kind = ActivityKind.Added, Level = ActivityLevel.Info, Title = series.Title, Message = $"Added to {category.ToString().ToLowerInvariant()}", SeriesId = series.Id },
            cancellationToken).ConfigureAwait(false);
        return series;
    }

    private async Task<Movie> CreateMovieAsync(int tmdbId, int? qualityProfileId, ReleaseVersion? version, CancellationToken cancellationToken)
    {
        int profileId = qualityProfileId ?? options.CurrentValue.Media.Movies.ProfileId;
        await EnsureProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        MovieInfo info = await movieMetadata.GetMovieAsync(tmdbId, cancellationToken).ConfigureAwait(false);
        Movie movie = ToMovie(info, profileId, options.CurrentValue.Paths);
        movie.Version = version;
        db.Movies.Add(movie);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await activities.RecordAsync(
            new Activity { Kind = ActivityKind.Added, Level = ActivityLevel.Info, Title = movie.Title, Message = "Added to movies", MovieId = movie.Id },
            cancellationToken).ConfigureAwait(false);
        return movie;
    }

    // Items added before the MediaPreferences migration have no original language, backdrop, rating or genres: fetch them once.
    // A source that is down or no longer knows an item leaves it for the next start. Returns the number of items filled.
    // shortcut: an item whose source has no original language is fetched again on every start, store a refreshed flag if libraries grow large.
    public async Task<int> BackfillMetadataAsync(CancellationToken cancellationToken)
    {
        int filled = 0;
        foreach (Movie movie in await db.Movies.Where(m => m.OriginalLanguage == Language.Unknown).ToListAsync(cancellationToken).ConfigureAwait(false))
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
        DeleteFolder(deleteFiles, series.Path);
        await db.Series.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await activities.RecordAsync(Removed(series.Title, deleteFiles, seriesId: id, movieId: null), cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteMovieAsync(int id, bool deleteFiles, CancellationToken cancellationToken)
    {
        Movie movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Movie {id} is not in the library.");
        DeleteFolder(deleteFiles, movie.Path);
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

    // Never a drive root or a library root (a blank title would make the root the item's folder).
    public static bool IsDeletableFolder(string path, PathOptions paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string[] roots = [paths.Series, paths.Movies, paths.Anime, paths.Downloads];
        return Path.GetDirectoryName(folder) is not null
            && !roots.Any(root =>
                string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), folder, StringComparison.OrdinalIgnoreCase));
    }

    public static SeriesType TypeOf(SeriesInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return info.IsDaily ? SeriesType.Daily
            : info.IsAnime ? SeriesType.Anime
            : SeriesType.Standard;
    }

    // Future monitors the episodes airing on or after the add day (or not dated yet); None leaves everything unmonitored.
    public static Series ToSeries(SeriesInfo info, int qualityProfileId, PathOptions paths, DateTime added, SeriesMonitor monitor = SeriesMonitor.All)
    {
        SeriesType type = TypeOf(info);
        DateOnly today = DateOnly.FromDateTime(added);
        int year = info.Year ?? 0;

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
            series.Seasons.Add(new Season { Number = season.SeasonNumber, Monitored = season.SeasonNumber > 0 && monitor != SeriesMonitor.None });
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
                Monitored = episode.SeasonNumber > 0 && monitor switch
                {
                    SeriesMonitor.All => true,
                    SeriesMonitor.Future => episode.AirDate is not DateOnly aired || aired >= today,
                    _ => false,
                },
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
    }

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

    // Files go before the rows, so a failed delete leaves the item in the library to retry.
    private void DeleteFolder(bool deleteFiles, string path)
    {
        if (!deleteFiles || !Directory.Exists(path))
        {
            return;
        }

        if (!IsDeletableFolder(path, options.CurrentValue.Paths))
        {
            throw new ArgumentException($"Refusing to delete '{path}': it is a root folder.", nameof(path));
        }

        Directory.Delete(path, recursive: true);
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
