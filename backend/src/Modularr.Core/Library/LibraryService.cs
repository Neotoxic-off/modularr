using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Metadata;
using Modularr.Core.Parsing.Movies;
using Modularr.Core.Persistence;

namespace Modularr.Core.Library;

// Adds series and movies from the metadata sources; the one place metadata records become library entities.
public sealed class LibraryService(ModularrDbContext db, SeriesMetadataClient seriesMetadata, MovieMetadataClient movieMetadata, IOptions<ModularrOptions> options)
{
    // Windows' set, so folder names are the same on every OS.
    private static readonly char[] InvalidNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public async Task<IReadOnlyList<Series>> ListSeriesAsync(CancellationToken cancellationToken) =>
        await db.Series.AsNoTracking().Include(s => s.Episodes).OrderBy(s => s.SortTitle).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Movie>> ListMoviesAsync(CancellationToken cancellationToken) =>
        await db.Movies.AsNoTracking().OrderBy(m => m.Title).ToListAsync(cancellationToken).ConfigureAwait(false);

    // Adding an existing series returns it unchanged.
    public async Task<Series> AddSeriesAsync(int tvdbId, int qualityProfileId, CancellationToken cancellationToken)
    {
        if (await db.Series.Include(s => s.Episodes).FirstOrDefaultAsync(s => s.TvdbId == tvdbId, cancellationToken).ConfigureAwait(false) is Series existing)
        {
            return existing;
        }

        await EnsureProfileAsync(qualityProfileId, cancellationToken).ConfigureAwait(false);
        SeriesInfo info = await seriesMetadata.GetSeriesAsync(tvdbId, cancellationToken: cancellationToken).ConfigureAwait(false);
        Series series = ToSeries(info, qualityProfileId, options.Value.Paths, DateTime.UtcNow);
        db.Series.Add(series);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return series;
    }

    public async Task<Movie> AddMovieAsync(int tmdbId, int qualityProfileId, CancellationToken cancellationToken)
    {
        if (await db.Movies.FirstOrDefaultAsync(m => m.TmdbId == tmdbId, cancellationToken).ConfigureAwait(false) is Movie existing)
        {
            return existing;
        }

        await EnsureProfileAsync(qualityProfileId, cancellationToken).ConfigureAwait(false);
        MovieInfo info = await movieMetadata.GetMovieAsync(tmdbId, cancellationToken).ConfigureAwait(false);
        Movie movie = ToMovie(info, qualityProfileId, options.Value.Paths);
        db.Movies.Add(movie);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return movie;
    }

    // Upstream has no anime flag in the metadata (the Sonarr user picks the type); the Anime genre is the closest signal.
    public static Series ToSeries(SeriesInfo info, int qualityProfileId, PathOptions paths, DateTime added)
    {
        SeriesType type = info.IsDaily ? SeriesType.Daily
            : info.Genres.Contains("Anime", StringComparer.OrdinalIgnoreCase) ? SeriesType.Anime
            : SeriesType.Standard;
        int year = info.Year ?? 0;

        Series series = new()
        {
            TvdbId = info.TvdbId,
            Title = info.Title,
            SortTitle = MovieTitleParser.NormalizeTitle(info.Title),
            Year = year,
            Runtime = info.Runtime,
            SeriesType = type,
            Monitored = true,
            Path = Path.Combine(type == SeriesType.Anime ? paths.Anime : paths.Series, FolderName(info.Title, year)),
            QualityProfileId = qualityProfileId,
            Added = added,
        };

        // Specials (season 0) are not monitored, like Sonarr's default.
        foreach (SeasonInfo season in info.Seasons)
        {
            series.Seasons.Add(new Season { Number = season.SeasonNumber, Monitored = season.SeasonNumber > 0 });
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
                Monitored = episode.SeasonNumber > 0,
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
    };

    // "Title (Year)" like the Sonarr/Radarr default folder format, invalid characters dropped.
    public static string FolderName(string title, int year)
    {
        string name = year > 0 ? $"{title} ({year})" : title;
        return string.Concat(name.Split(InvalidNameChars)).Trim().TrimEnd('.');
    }

    private async Task EnsureProfileAsync(int qualityProfileId, CancellationToken cancellationToken)
    {
        if (!await db.QualityProfiles.AnyAsync(p => p.Id == qualityProfileId, cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException($"Quality profile {qualityProfileId} does not exist.", nameof(qualityProfileId));
        }
    }
}
