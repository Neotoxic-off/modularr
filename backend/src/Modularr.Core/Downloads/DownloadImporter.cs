using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Languages;
using Modularr.Core.MediaFiles;
using Modularr.Core.Parsing;
using Modularr.Core.Parsing.Episodes;
using Modularr.Core.Persistence;
using Modularr.Core.PostProcessing;
using Modularr.Core.Qualities;
using Modularr.Core.Search;

namespace Modularr.Core.Downloads;

// Upstream CompletedDownloadService + ImportApprovedEpisodes/Movies + FailedDownloadService for one finished torrent.
public sealed class DownloadImporter(ModularrDbContext db, IOptions<ModularrOptions> options)
{
    // Returns true when the release was rejected and blocklisted: the caller then removes the torrent and its data.
    public async Task<bool> ImportAsync(TrackedDownload download, string outputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(download);
        download.State = TrackedDownloadState.Importing;
        PostProcessingVerdict verdict = CompletedDownloadProcessor.Process(outputPath, options.Value.PostProcessing);

        switch (verdict)
        {
            case PostProcessingVerdict.Accepted accepted:
                // No video matched an episode of the series: leave it for the user rather than import nothing silently.
                download.State = await ImportFilesAsync(download, accepted, cancellationToken).ConfigureAwait(false) > 0
                    ? TrackedDownloadState.Imported
                    : TrackedDownloadState.ImportBlocked;
                break;

            case PostProcessingVerdict.Rejected { Blacklist: true } rejected:
                await FailAsync(download, rejected.Reason, cancellationToken).ConfigureAwait(false);
                return true;

            // A missing or linked download path: nothing wrong with the release itself, wait for the user.
            default:
                download.State = TrackedDownloadState.ImportBlocked;
                break;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return false;
    }

    // Upstream FailedDownloadService.MarkAsFailed: blocklists the release, records the failure and keeps the reason.
    public async Task FailAsync(TrackedDownload download, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(download);
        await BlocklistAsync(download, reason, cancellationToken).ConfigureAwait(false);
        download.State = TrackedDownloadState.Failed;
        download.Message = reason;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // shortcut: files keep their release file name and replaced files stay on disk, add renaming and the recycle bin later.
    private async Task<int> ImportFilesAsync(TrackedDownload download, PostProcessingVerdict.Accepted accepted, CancellationToken cancellationToken)
    {
        Series? series = download.SeriesId is int seriesId
            ? await db.Series.Include(s => s.Episodes).SingleAsync(s => s.Id == seriesId, cancellationToken).ConfigureAwait(false)
            : null;
        Movie? movie = download.MovieId is int movieId ? await db.Movies.SingleAsync(m => m.Id == movieId, cancellationToken).ConfigureAwait(false) : null;
        string? firstDirectory = null;
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

            if (!File.Exists(destination))
            {
                FileLinker.LinkOrCopy(video, destination);
            }

            imported++;

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
            string destination = Path.Combine(firstDirectory!, Path.GetFileName(subtitle));
            if (!File.Exists(destination))
            {
                FileLinker.LinkOrCopy(subtitle, destination);
            }
        }

        return imported;
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
