using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Modularr.Core.Decisions;
using Modularr.Core.Persistence;

namespace Modularr.Core.Search;

// A manual search or grab for what already has a file; the API answers 409.
public sealed class AlreadyPresentException(string message) : Exception(message);

// The one rule for whether an item is still searched and grabbed: it has no file, or upgrading is asked and its file is below the
// profile's cutoff. Automatic searches always upgrade; manual ones only when asked.
public static class PresentFiles
{
    public static bool IsWanted(MediaFile? file, QualityProfile profile, bool upgrade)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return file is null || (upgrade && UpgradableSpecification.QualityCutoffNotMet(profile, file.Quality));
    }

    public static async Task EnsureMovieWantedAsync(ModularrDbContext db, int movieId, bool upgrade, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        Movie movie = await db.Movies.AsNoTracking().Include(m => m.QualityProfile).FirstOrDefaultAsync(m => m.Id == movieId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Movie {movieId} is not in the library.");
        MediaFile? file = await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(f => f.MovieId == movieId, cancellationToken).ConfigureAwait(false);
        if (!IsWanted(file, movie.QualityProfile, upgrade))
        {
            throw new AlreadyPresentException(Refusal(movie.Title, upgrade));
        }
    }

    // Refused only when the series has episodes in scope and none of them is wanted.
    public static async Task EnsureEpisodesWantedAsync(
        ModularrDbContext db, int seriesId, Expression<Func<Episode, bool>> scope, string what, bool upgrade, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        QualityProfile profile = await db.Series.AsNoTracking().Where(s => s.Id == seriesId).Select(s => s.QualityProfile).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Series {seriesId} is not in the library.");
        List<int?> fileIds = await db.Episodes.AsNoTracking().Where(e => e.SeriesId == seriesId).Where(scope).Select(e => e.MediaFileId).ToListAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<int, MediaFile> files = await db.MediaFiles.AsNoTracking().Where(f => f.SeriesId == seriesId).ToDictionaryAsync(f => f.Id, cancellationToken).ConfigureAwait(false);
        if (fileIds.Count > 0 && !fileIds.Any(id => IsWanted(id is int fileId ? files.GetValueOrDefault(fileId) : null, profile, upgrade)))
        {
            throw new AlreadyPresentException(Refusal(what, upgrade));
        }
    }

    private static string Refusal(string what, bool upgrade) => upgrade
        ? $"{what} already has a file that meets the quality cutoff."
        : $"{what} already has a file. Ask for an upgrade to replace a file below the quality cutoff.";
}
