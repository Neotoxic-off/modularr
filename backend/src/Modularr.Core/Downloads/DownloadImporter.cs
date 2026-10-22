using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Languages;
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
                if (await ImportFilesAsync(download, accepted, cancellationToken).ConfigureAwait(false) is string imported)
                {
                    download.State = TrackedDownloadState.Imported;
                    activities.Add(Activity.For(download, ActivityKind.Imported, ActivityLevel.Success, imported));
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
    public async Task FailAsync(TrackedDownload download, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(download);
        await BlocklistAsync(download, reason, cancellationToken).ConfigureAwait(false);
        download.State = TrackedDownloadState.Failed;
        download.Message = reason;
        activities.Add(Activity.For(download, ActivityKind.DownloadFailed, ActivityLevel.Error, reason));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Block(TrackedDownload download, string reason)
    {
        download.State = TrackedDownloadState.ImportBlocked;
        activities.Add(Activity.For(download, ActivityKind.ImportFailed, ActivityLevel.Error, reason));
    }

    // What was imported ("Imported to <file>" or "<n> files imported to <folder>", "(moved)" after imported in Move mode), null when
    // nothing was. A file already at the destination is kept, and so is its source.
    // shortcut: files keep their release file name and replaced files stay on disk, add renaming and the recycle bin later.
    private async Task<string?> ImportFilesAsync(TrackedDownload download, PostProcessingVerdict.Accepted accepted, CancellationToken cancellationToken)
    {
        bool move = options.CurrentValue.PostProcessing.ImportMode == ImportMode.Move;
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
            string destination = Path.Combine(directory, fileName);
            firstDirectory ??= directory;

            Transfer(video, destination);
            imported++;
            lastFile = destination;

            if (await db.MediaFiles.AnyAsync(f => f.Path == destination, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            QualityModel quality = QualityParser.ParseQuality(fileName) is { Quality.Id: not 0 } fromFile ? fromFile : QualityParser.ParseQuality(download.ReleaseTitle);
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

            foreach (Episode episode in episodes)
            {
                episode.MediaFileId = file.Id;
            }

            foreach (int? episodeId in episodes.Count == 0 ? [null] : episodes.Select(e => (int?)e.Id))
            {
                AddHistory(download, HistoryEventType.Imported, episodeId, quality, file.Languages).Data["importedPath"] = destination;
            }
        }

        // shortcut: subtitles go next to the first imported video, match them per video when packs carry several.
        foreach (string subtitle in accepted.SubtitleFiles.Where(_ => firstDirectory is not null))
        {
            Transfer(subtitle, Path.Combine(firstDirectory!, Path.GetFileName(subtitle)));
        }

        string how = move ? " (moved)" : "";
        return imported switch
        {
            0 => null,
            1 => $"Imported{how} to {lastFile}",
            _ => $"{imported} files imported{how} to {firstDirectory}",
        };
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
