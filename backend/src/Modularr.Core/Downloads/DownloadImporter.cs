using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Languages;
using Modularr.Core.Library;
using Modularr.Core.MediaFiles;
using Modularr.Core.Parsing;
using Modularr.Core.Parsing.Episodes;
using Modularr.Core.Persistence;
using Modularr.Core.PostProcessing;
using Modularr.Core.Qualities;
using Modularr.Core.Search;
using Modularr.Core.Subtitles;

namespace Modularr.Core.Downloads;

// Upstream CompletedDownloadService + ImportApprovedEpisodes/Movies + FailedDownloadService for one finished torrent.
public sealed class DownloadImporter(ModularrDbContext db, ActivityLog activities, IOptionsMonitor<ModularrOptions> options, SubtitleRunner subtitles)
{
    // Files of this import, handed to the subtitle runner once their episodes are saved.
    private readonly List<MediaFile> _imported = [];

    // Returns true when the release was rejected and blocklisted: the caller then removes the torrent and its data.
    public async Task<bool> ImportAsync(TrackedDownload download, string outputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(download);
        download.State = TrackedDownloadState.Importing;
        PostProcessingVerdict verdict = CompletedDownloadProcessor.Process(outputPath, options.CurrentValue.PostProcessing);

        switch (verdict)
        {
            case PostProcessingVerdict.Accepted accepted:
                // No video matched an episode of the series: leave it for the user rather than import nothing silently.
                if (await ImportFilesAsync(download, accepted, cancellationToken).ConfigureAwait(false) is (string imported, string folder))
                {
                    download.State = TrackedDownloadState.Imported;
                    Activity activity = Activity.For(download, ActivityKind.Imported, ActivityLevel.Success, imported);
                    activity.Path = folder;
                    activities.Add(activity);
                }
                else
                {
                    Block(download, "No video file matches an episode of the series");
                }

                break;

            case PostProcessingVerdict.Rejected { Blacklist: true } rejected:
                await FailAsync(download, rejected.Reason, cancellationToken).ConfigureAwait(false);
                return true;

            // A missing or linked download path: nothing wrong with the release itself, wait for the user.
            case PostProcessingVerdict.Rejected rejected:
                Block(download, rejected.Reason);
                break;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _imported.ForEach(file => subtitles.Enqueue(file.Id));
        _imported.Clear();
        return false;
    }

    // Upstream FailedDownloadService.MarkAsFailed: blocklists the release, records the failure and keeps the reason.
    public async Task FailAsync(TrackedDownload download, string reason, CancellationToken cancellationToken, bool blocklist = true)
    {
        ArgumentNullException.ThrowIfNull(download);
        if (blocklist)
        {
            await BlocklistAsync(download, reason, cancellationToken).ConfigureAwait(false);
        }

        download.State = TrackedDownloadState.Failed;
        download.Message = reason;
        activities.Add(Activity.For(download, ActivityKind.DownloadFailed, ActivityLevel.Error, reason));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Block(TrackedDownload download, string reason)
    {
        download.State = TrackedDownloadState.ImportBlocked;
        download.Message = reason;
        activities.Add(Activity.For(download, ActivityKind.ImportFailed, ActivityLevel.Error, reason));
    }

    // What was imported ("Imported to <file>" or "<n> files imported to <folder>", "(moved)" after imported in Move mode), null when
    // nothing was, with the title's folder. A file already at the destination is kept, and so is its source. With renaming on, a clean name already taken
    // keeps the release's name. Files this import replaces leave the library (RemoveReplacedAsync).
    private async Task<(string Imported, string Folder)?> ImportFilesAsync(TrackedDownload download, PostProcessingVerdict.Accepted accepted, CancellationToken cancellationToken)
    {
        bool move = options.CurrentValue.PostProcessing.ImportMode == ImportMode.Move;
        bool rename = options.CurrentValue.PostProcessing.RenameFiles;
        void Transfer(string source, string destination)
        {
            if (File.Exists(destination))
            {
                return;
            }

            if (move)
            {
                FileLinker.Move(source, destination);
            }
            else
            {
                FileLinker.LinkOrCopy(source, destination);
            }
        }

        Series? series = download.SeriesId is int seriesId
            ? await db.Series.Include(s => s.Episodes).SingleAsync(s => s.Id == seriesId, cancellationToken).ConfigureAwait(false)
            : null;
        Movie? movie = download.MovieId is int movieId ? await db.Movies.SingleAsync(m => m.Id == movieId, cancellationToken).ConfigureAwait(false) : null;
        string? firstDirectory = null;
        string? lastFile = null;
        int imported = 0;
        List<(string Source, string Destination)> videos = [];

        foreach (string video in accepted.VideoFiles)
        {
            string fileName = Path.GetFileName(video);
            IReadOnlyList<Episode> episodes = series is null ? [] : EpisodesOf(series, fileName, download, accepted.VideoFiles.Count);
            if (series is not null && episodes.Count == 0)
            {
                continue;
            }

            string directory = series is null
                ? movie!.Path
                : Path.Combine(series.Path, string.Create(CultureInfo.InvariantCulture, $"Season {episodes[0].SeasonNumber:00}"));
            QualityModel quality = QualityParser.ParseQuality(fileName) is { Quality.Id: not 0 } fromFile ? fromFile : QualityParser.ParseQuality(download.ReleaseTitle);
            string destination = Path.Combine(directory, fileName);
            string clean = !rename ? "" : series is null ? FileNamer.ForMovie(movie!, quality.Quality) : FileNamer.ForEpisodes(series, episodes, quality.Quality);
            if (clean.Length > 0 && Path.Combine(directory, clean + Path.GetExtension(fileName)) is string renamed && !File.Exists(renamed))
            {
                destination = renamed;
            }

            firstDirectory ??= directory;

            Transfer(video, destination);
            imported++;
            lastFile = destination;
            videos.Add((video, destination));

            if (await db.MediaFiles.AnyAsync(f => f.Path == destination, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            MediaFile file = new()
            {
                SeriesId = series?.Id,
                MovieId = movie?.Id,
                Path = destination,
                Size = new FileInfo(destination).Length,
                Quality = quality,
                Languages = LanguageParser.ParseLanguages(download.ReleaseTitle),
                ReleaseGroup = ReleaseGroupParser.ParseReleaseGroup(download.ReleaseTitle),
                DateAdded = DateTime.UtcNow,
            };
            db.MediaFiles.Add(file);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _imported.Add(file);

            List<int> replaced = series is null
                ? await db.MediaFiles.Where(f => f.MovieId == movie!.Id && f.Id != file.Id).Select(f => f.Id).ToListAsync(cancellationToken).ConfigureAwait(false)
                : [.. episodes.Select(e => e.MediaFileId).OfType<int>()];
            foreach (Episode episode in episodes)
            {
                episode.MediaFileId = file.Id;
            }

            await RemoveReplacedAsync(download, replaced, series?.Path ?? movie!.Path, cancellationToken).ConfigureAwait(false);

            foreach (int? episodeId in episodes.Count == 0 ? [null] : episodes.Select(e => (int?)e.Id))
            {
                AddHistory(download, HistoryEventType.Imported, episodeId, quality, file.Languages).Data["importedPath"] = destination;
            }
        }

        // A sidecar named after a video follows it and its new name ("<name>.en.srt"); any other goes next to the first video, after
        // the only video's new name when renaming.
        foreach (string subtitle in accepted.SubtitleFiles.Where(_ => firstDirectory is not null))
        {
            string name = Path.GetFileName(subtitle);
            (string Source, string Destination) owner = videos.Find(v => name.StartsWith(Path.GetFileNameWithoutExtension(v.Source) + ".", StringComparison.OrdinalIgnoreCase));
            string destination = owner.Source is not null
                ? Path.Combine(Path.GetDirectoryName(owner.Destination)!, Path.GetFileNameWithoutExtension(owner.Destination) + name[Path.GetFileNameWithoutExtension(owner.Source).Length..])
                : Path.Combine(firstDirectory!, (rename && videos.Count == 1 ? Path.GetFileNameWithoutExtension(videos[0].Destination) + "." : "") + name);
            Transfer(subtitle, destination);
        }

        string how = move ? " (moved)" : "";
        string titleFolder = series?.Path ?? movie!.Path;
        return imported switch
        {
            0 => null,
            1 => ($"Imported{how} to {lastFile}", titleFolder),
            _ => ($"{imported} files imported{how} to {firstDirectory}", titleFolder),
        };
    }

    // Upstream UpgradeMediaFileService: a replaced file no episode uses any more leaves the library with its row. It is deleted (or
    // moved to the recycle bin) only inside the title's folder, reached through no link and not the path of a file just imported.
    private async Task RemoveReplacedAsync(TrackedDownload download, List<int> fileIds, string titleFolder, CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (int id in fileIds.Distinct().Where(id => !_imported.Exists(f => f.Id == id)))
        {
            if (await db.Episodes.AnyAsync(e => e.MediaFileId == id, cancellationToken).ConfigureAwait(false)
                || await db.MediaFiles.FindAsync([id], cancellationToken).ConfigureAwait(false) is not MediaFile old)
            {
                continue;
            }

            db.MediaFiles.Remove(old);
            if (!File.Exists(old.Path))
            {
                continue;
            }

            string? kept = !LibraryPaths.IsInside(old.Path, titleFolder)
                || LibraryPaths.ScannableRoot(Path.GetDirectoryName(Path.GetFullPath(old.Path))!, options.CurrentValue.Paths) is null
                || new FileInfo(old.Path).LinkTarget is not null
                || _imported.Exists(f => string.Equals(LibraryPaths.Normalize(f.Path), LibraryPaths.Normalize(old.Path), LibraryPaths.Comparison))
                ? "it is not a file of the title's folder" : null;
            try
            {
                if (kept is null)
                {
                    Discard(old.Path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                kept = ex.Message;
            }

            if (kept is not null)
            {
                activities.Add(Activity.For(download, ActivityKind.Imported, ActivityLevel.Warning, $"Kept the replaced file {old.Path}: {kept}"));
            }
        }
    }

    private void Discard(string path)
    {
        string recycleBin = options.CurrentValue.PostProcessing.RecycleBinPath;
        if (string.IsNullOrWhiteSpace(recycleBin))
        {
            File.Delete(path);
            return;
        }

        Directory.CreateDirectory(recycleBin);
        File.Move(path, FileNamer.Unused(Path.Combine(recycleBin, Path.GetFileName(path))));
    }

    // After a move: the download's folder and its subfolders once empty, only strictly inside the downloads root and never through a
    // link. A single-file download has no folder of its own and leaves nothing.
    public static void DeleteEmptyFolders(string outputPath, string downloadsRoot)
    {
        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputPath));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(downloadsRoot)) + Path.DirectorySeparatorChar;
        if (!folder.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || !Directory.Exists(folder) || new DirectoryInfo(folder).LinkTarget is not null)
        {
            return;
        }

        foreach (string child in Directory.GetDirectories(folder).Where(child => new DirectoryInfo(child).LinkTarget is null))
        {
            DeleteEmptyFolders(child, downloadsRoot);
        }

        if (!Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Directory.Delete(folder);
        }
    }

    // The file's own name first (season packs); a single-video download falls back to the episodes it was grabbed for.
    private static IReadOnlyList<Episode> EpisodesOf(Series series, string fileName, TrackedDownload download, int videoCount)
    {
        IReadOnlyList<Episode> parsed = EpisodeParser.ParseTitle(fileName) is ParsedEpisodeInfo info ? ReleaseDecisions.MapEpisodes(info, series.Episodes) : [];
        return parsed.Count > 0 || videoCount > 1 ? parsed : [.. series.Episodes.Where(e => download.EpisodeIds.Contains(e.Id))];
    }

    public async Task BlocklistAsync(TrackedDownload download, string reason, CancellationToken cancellationToken)
    {
        QualityModel quality = QualityParser.ParseQuality(download.ReleaseTitle);
        IReadOnlyList<Language> languages = LanguageParser.ParseLanguages(download.ReleaseTitle);

        if (!await db.Blocklist.AnyAsync(b => b.InfoHash == download.InfoHash, cancellationToken).ConfigureAwait(false))
        {
            activities.Add(Activity.For(download, ActivityKind.Blocklisted, ActivityLevel.Warning, reason));
            db.Blocklist.Add(new BlocklistItem
            {
                SeriesId = download.SeriesId,
                MovieId = download.MovieId,
                EpisodeIds = download.EpisodeIds,
                SourceTitle = download.ReleaseTitle,
                Quality = quality,
                Languages = languages,
                InfoHash = download.InfoHash,
                Indexer = download.Indexer,
                Reason = reason,
                Date = DateTime.UtcNow,
            });
        }

        foreach (int? episodeId in download.EpisodeIds.Count == 0 ? [null] : download.EpisodeIds.Select(id => (int?)id))
        {
            AddHistory(download, HistoryEventType.Failed, episodeId, quality, languages).Data["reason"] = reason;
        }
    }

    public HistoryItem AddHistory(TrackedDownload download, HistoryEventType type, int? episodeId, QualityModel quality, IReadOnlyList<Language> languages)
    {
        HistoryItem history = new()
        {
            EventType = type,
            SeriesId = download.SeriesId,
            EpisodeId = episodeId,
            MovieId = download.MovieId,
            SourceTitle = download.ReleaseTitle,
            Quality = quality,
            Languages = languages,
            InfoHash = download.InfoHash,
            Date = DateTime.UtcNow,
        };
        db.History.Add(history);
        return history;
    }
}
