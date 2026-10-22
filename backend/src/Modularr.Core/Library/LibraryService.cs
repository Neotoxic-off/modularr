using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Languages;
using Modularr.Core.Metadata;
using Modularr.Core.Parsing.Movies;
using Modularr.Core.Persistence;

namespace Modularr.Core.Library;

// Sonarr's add-series monitor choices that matter here. Specials are never monitored.
public enum SeriesMonitor
{
    All,
    Future,
    None,
}

// PATCH body: an absent field is left unchanged; Language present as null clears the item's override.
public sealed record ItemUpdate
{
    public int? QualityProfileId { get; init; }
    public bool? Monitored { get; init; }

    public Language? Language
    {
        get;
        init
        {
            field = value;
            LanguageSet = true;
        }
    }

    // The JSON reader calls the setter only for a field present in the body.
    [JsonIgnore]
    public bool LanguageSet { get; private init; }
}

// Adds series and movies from the metadata sources; the one place metadata records become library entities.
public sealed class LibraryService(ModularrDbContext db, SeriesMetadataClient seriesMetadata, MovieMetadataClient movieMetadata, IOptionsMonitor<ModularrOptions> options)
{
    // Windows' set, so folder names are the same on every OS.
    private static readonly char[] InvalidNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public async Task<IReadOnlyList<Series>> ListSeriesAsync(CancellationToken cancellationToken) =>
        await db.Series.AsNoTracking().Include(s => s.Episodes).OrderBy(s => s.SortTitle).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Movie>> ListMoviesAsync(CancellationToken cancellationToken) =>
        await db.Movies.AsNoTracking().OrderBy(m => m.Title).ToListAsync(cancellationToken).ConfigureAwait(false);

    // Adding an existing series returns it unchanged. No profile: the category default (series or anime) from MediaOptions.
    public async Task<Series> AddSeriesAsync(int tvdbId, int? qualityProfileId, Language? language, SeriesMonitor monitor, CancellationToken cancellationToken)
    {
        if (await db.Series.Include(s => s.Episodes).FirstOrDefaultAsync(s => s.TvdbId == tvdbId, cancellationToken).ConfigureAwait(false) is Series existing)
        {
            return existing;
        }

        SeriesInfo info = await seriesMetadata.GetSeriesAsync(tvdbId, cancellationToken: cancellationToken).ConfigureAwait(false);
        MediaType category = TypeOf(info) == SeriesType.Anime ? MediaType.Anime : MediaType.Series;
        int profileId = qualityProfileId ?? options.CurrentValue.Media.For(category).ProfileId;
        await EnsureProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        Series series = ToSeries(info, profileId, options.CurrentValue.Paths, DateTime.UtcNow, monitor);
        series.Language = language;
        db.Series.Add(series);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return series;
    }

    public async Task<Movie> AddMovieAsync(int tmdbId, int? qualityProfileId, Language? language, CancellationToken cancellationToken)
    {
        if (await db.Movies.FirstOrDefaultAsync(m => m.TmdbId == tmdbId, cancellationToken).ConfigureAwait(false) is Movie existing)
        {
            return existing;
        }

        int profileId = qualityProfileId ?? options.CurrentValue.Media.Movies.ProfileId;
        await EnsureProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        MovieInfo info = await movieMetadata.GetMovieAsync(tmdbId, cancellationToken).ConfigureAwait(false);
        Movie movie = ToMovie(info, profileId, options.CurrentValue.Paths);
        movie.Language = language;
        db.Movies.Add(movie);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return movie;
    }

    // Monitored applies to the series only, its seasons and episodes keep their own flags.
    public async Task<Series> UpdateSeriesAsync(int id, ItemUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        Series series = await db.Series.Include(s => s.Episodes).FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Series {id} is not in the library.");
        series.QualityProfileId = await ProfileIdAsync(update, series.QualityProfileId, cancellationToken).ConfigureAwait(false);
        series.Monitored = update.Monitored ?? series.Monitored;
        series.Language = update.LanguageSet ? update.Language : series.Language;
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
        movie.Language = update.LanguageSet ? update.Language : movie.Language;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return movie;
    }

    public async Task DeleteSeriesAsync(int id, bool deleteFiles, CancellationToken cancellationToken)
    {
        IQueryable<Series> series = db.Series.Where(s => s.Id == id);
        string path = await series.Select(s => s.Path).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Series {id} is not in the library.");
        DeleteFolder(deleteFiles, path);
        await series.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteMovieAsync(int id, bool deleteFiles, CancellationToken cancellationToken)
    {
        IQueryable<Movie> movie = db.Movies.Where(m => m.Id == id);
        string path = await movie.Select(m => m.Path).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Movie {id} is not in the library.");
        DeleteFolder(deleteFiles, path);
        await movie.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

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

    // Upstream has no anime flag in the metadata (the Sonarr user picks the type); the Anime genre is the closest signal.
    public static SeriesType TypeOf(SeriesInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return info.IsDaily ? SeriesType.Daily
            : info.Genres.Contains("Anime", StringComparer.OrdinalIgnoreCase) ? SeriesType.Anime
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
            OriginalLanguage = info.OriginalLanguage,
            Overview = info.Overview,
            Poster = PosterOf(info.Images),
        };

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

    public static Movie ToMovie(MovieInfo info, int qualityProfileId, PathOptions paths) => new()
    {
        TmdbId = info.TmdbId,
        ImdbId = info.ImdbId,
        Title = info.Title,
        Year = info.Year,
        Runtime = info.Runtime,
        Monitored = true,
        Path = Path.Combine(paths.Movies, FolderName(info.Title, info.Year)),
        QualityProfileId = qualityProfileId,
        OriginalLanguage = info.OriginalLanguage,
        Overview = info.Overview,
        Poster = PosterOf(info.Images),
    };

    // "Title (Year)" like the Sonarr/Radarr default folder format, invalid characters dropped.
    public static string FolderName(string title, int year)
    {
        string name = year > 0 ? $"{title} ({year})" : title;
        return string.Concat(name.Split(InvalidNameChars)).Trim().TrimEnd('.');
    }

    private static string? PosterOf(IReadOnlyList<MetadataImage> images) => images.FirstOrDefault(i => i.CoverType == MediaCoverType.Poster)?.Url;

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
