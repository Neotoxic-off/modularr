using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Languages;
using Modularr.Core.Parsing;
using Modularr.Core.Parsing.Episodes;
using Modularr.Core.Parsing.Movies;
using Modularr.Core.Persistence;
using Modularr.Core.PostProcessing;
using Modularr.Core.Qualities;
using Modularr.Core.Search;

namespace Modularr.Core.Library;

// Items is the number of titles whose folder was scanned; a missing, linked or out-of-root folder is skipped.
public sealed record ScanResult(int Items, int Added, int Removed);

// Upstream DiskScanService: reconciles a title's MediaFile rows with the videos in its folder. A file found is added, a row whose
// file is gone is deleted, the rows still on disk are kept. Files are only read, never moved or deleted.
public sealed class DiskScanService(ModularrDbContext db, ActivityLog activities, IOptionsMonitor<ModularrOptions> options)
{
    // Season folders and an extras or release folder inside them.
    private const int MaxDepth = 3;

    // Scans add rows by path (a unique index): one at a time across the app.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    // Links are never followed: one could lead out of the library roots.
    private static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = MaxDepth,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    // record: always report the total (a scan asked for by hand); otherwise only when something changed.
    public async Task<ScanResult> ScanAllAsync(bool record, CancellationToken cancellationToken)
    {
        List<int> seriesIds = await db.Series.Select(s => s.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<int> movieIds = await db.Movies.Select(m => m.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<ScanResult> results = [];
        foreach (int id in seriesIds)
        {
            results.Add(await ScanSeriesAsync(id, record: true, cancellationToken).ConfigureAwait(false));
            db.ChangeTracker.Clear();
        }

        foreach (int id in movieIds)
        {
            results.Add(await ScanMovieAsync(id, record: true, cancellationToken).ConfigureAwait(false));
            db.ChangeTracker.Clear();
        }

        ScanResult total = new(results.Sum(r => r.Items), results.Sum(r => r.Added), results.Sum(r => r.Removed));
        if (!record && total.Added + total.Removed == 0)
        {
            return total;
        }

        await activities.RecordAsync(
            new Activity { Kind = ActivityKind.ScanCompleted, Level = ActivityLevel.Info, Title = "Library", Message = $"{total.Items} title(s) scanned, {Counts(total)}" },
            cancellationToken).ConfigureAwait(false);
        return total;
    }

    // An episode keeps the file it has; a file matching no episode without one is left alone.
    public async Task<ScanResult> ScanSeriesAsync(int id, bool record, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Series series = await db.Series.Include(s => s.Episodes).FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Series {id} is not in the library.");
            List<MediaFile> rows = await db.MediaFiles.Where(f => f.SeriesId == id).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (VideoFiles(series.Path) is not { } videos)
            {
                return new ScanResult(0, 0, 0);
            }

            int removed = RemoveMissing(rows);
            int added = 0;
            foreach (string video in videos.Where(v => !rows.Exists(r => string.Equals(r.Path, v, LibraryPaths.Comparison))))
            {
                ParsedEpisodeInfo? parsed = EpisodeParser.ParsePath(video);
                Episode[] episodes = parsed is null ? [] : [.. ReleaseDecisions.MapEpisodes(parsed, series.Episodes).Where(e => e.MediaFileId is null)];
                if (parsed is null || episodes.Length == 0)
                {
                    continue;
                }

                MediaFile file = NewFile(video, parsed.Quality, parsed.Languages, parsed.ReleaseGroup);
                file.SeriesId = id;
                db.MediaFiles.Add(file);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                foreach (Episode episode in episodes)
                {
                    episode.MediaFileId = file.Id;
                }

                added++;
            }

            return await SaveAsync(new ScanResult(1, added, removed), record, series.Title, id, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    // A movie has one file: the largest video found, when the movie has none left.
    public async Task<ScanResult> ScanMovieAsync(int id, bool record, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Movie movie = await db.Movies.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Movie {id} is not in the library.");
            List<MediaFile> rows = await db.MediaFiles.Where(f => f.MovieId == id).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (VideoFiles(movie.Path) is not { } videos)
            {
                return new ScanResult(0, 0, 0);
            }

            int removed = RemoveMissing(rows);
            int added = 0;
            if (rows.Count == removed && videos.MaxBy(v => new FileInfo(v).Length) is string video)
            {
                ParsedMovieInfo? parsed = MovieTitleParser.ParseMoviePath(video);
                string name = Path.GetFileName(video);
                MediaFile file = NewFile(
                    video, parsed?.Quality ?? QualityParser.ParseQuality(name), parsed?.Languages ?? LanguageParser.ParseLanguages(name), parsed?.ReleaseGroup);
                file.MovieId = id;
                db.MediaFiles.Add(file);
                added++;
            }

            return await SaveAsync(new ScanResult(1, added, removed), record, movie.Title, null, id, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<ScanResult> SaveAsync(ScanResult result, bool record, string title, int? seriesId, int? movieId, CancellationToken cancellationToken)
    {
        if (record && result.Added + result.Removed > 0)
        {
            activities.Add(new Activity
            {
                Kind = ActivityKind.ScanCompleted, Level = ActivityLevel.Info, Title = title, Message = Counts(result), SeriesId = seriesId, MovieId = movieId,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static string Counts(ScanResult result) => $"{result.Added} file(s) found, {result.Removed} missing";

    // The episodes of a deleted row lose their file through the foreign key.
    private int RemoveMissing(List<MediaFile> rows)
    {
        MediaFile[] gone = [.. rows.Where(r => IsGone(r.Path))];
        db.MediaFiles.RemoveRange(gone);
        return gone.Length;
    }

    // Only a file known not to exist is gone: File.Exists is also false on access denied or a share hiccup.
    private static bool IsGone(string path)
    {
        try
        {
            File.GetAttributes(path);
            return false;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Null when the folder must not be scanned: outside the roots, reached through a link, or missing (an unmounted disk must not
    // look like every file deleted).
    private List<string>? VideoFiles(string folder)
    {
        ModularrOptions settings = options.CurrentValue;
        if (LibraryPaths.ScannableRoot(folder, settings.Paths) is null || !Directory.Exists(folder))
        {
            return null;
        }

        try
        {
            return
            [
                .. Directory.EnumerateFiles(folder, "*", Walk)
                    .Where(f => FileClassifier.Classify(f, settings.PostProcessing) == FileKind.Video && !SampleDetector.IsSample(f, new FileInfo(f).Length)),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static MediaFile NewFile(string path, QualityModel quality, IReadOnlyList<Language> languages, string? releaseGroup) => new()
    {
        Path = path,
        Size = new FileInfo(path).Length,
        Quality = quality,
        Languages = languages,
        ReleaseGroup = releaseGroup,
        DateAdded = DateTime.UtcNow,
    };
}
