using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Downloads.Torrents;
using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Cardigann;
using Modularr.Core.Indexers.Catalog;
using Modularr.Core.Languages;
using Modularr.Core.Parsing;
using Modularr.Core.Parsing.Episodes;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;
using Modularr.Core.Search;

namespace Modularr.Core.Downloads;

// A search result to download for a series (its episodes are mapped from the title) or a movie of the library.
public sealed record GrabRequest
{
    public required ReleaseInfo Release { get; init; }
    public int? SeriesId { get; init; }
    public int? MovieId { get; init; }
}

public sealed record DownloadItem(TrackedDownload Download, TorrentStatus? Torrent);

// Upstream DownloadService.DownloadReport + TrackedDownloadService: hands the release to the torrent engine and records the grab.
public sealed class DownloadService(
    ModularrDbContext db,
    TorrentEngineHost torrents,
    IndexerCatalogHost catalog,
    IndexerDomainHealth health,
    CardigannSearcher searcher,
    IHttpClientFactory httpClientFactory,
    IOptions<ModularrOptions> options)
{
    public const string SeriesCategory = "series";
    public const string MoviesCategory = "movies";

    public async Task<IReadOnlyList<DownloadItem>> ListAsync(CancellationToken cancellationToken)
    {
        List<TrackedDownload> downloads = await db.TrackedDownloads.AsNoTracking().OrderByDescending(d => d.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. downloads.Select(d => new DownloadItem(d, torrents.Engine.Get(d.InfoHash)))];
    }

    public async Task<TrackedDownload> GrabAsync(GrabRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ReleaseInfo release = request.Release;
        if ((request.SeriesId is null) == (request.MovieId is null))
        {
            throw new ArgumentException("Grab for exactly one series or movie of the library.", nameof(request));
        }

        IReadOnlyList<Episode> episodes = [];
        if (request.SeriesId is int seriesId)
        {
            Series series = await db.Series.Include(s => s.Episodes).FirstOrDefaultAsync(s => s.Id == seriesId, cancellationToken).ConfigureAwait(false)
                ?? throw new ArgumentException($"Series {seriesId} is not in the library.", nameof(request));
            episodes = EpisodeParser.ParseTitle(release.Title) is ParsedEpisodeInfo parsed ? ReleaseDecisions.MapEpisodes(parsed, series.Episodes) : [];
        }
        else if (!await db.Movies.AnyAsync(m => m.Id == request.MovieId, cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException($"Movie {request.MovieId} is not in the library.", nameof(request));
        }

        string category = request.SeriesId is null ? MoviesCategory : SeriesCategory;
        string id = await AddTorrentAsync(release, Path.Combine(options.Value.Paths.Downloads, category), category, cancellationToken).ConfigureAwait(false);

        if (await db.TrackedDownloads.FirstOrDefaultAsync(d => d.InfoHash == id, cancellationToken).ConfigureAwait(false) is TrackedDownload existing)
        {
            // A failed download is blocklisted and never polled again: the torrent just added back would run untracked.
            if (existing.State == TrackedDownloadState.Failed)
            {
                await torrents.Engine.RemoveAsync(id, deleteData: true).ConfigureAwait(false);
            }

            return existing;
        }

        TrackedDownload download = new()
        {
            InfoHash = id,
            SeriesId = request.SeriesId,
            MovieId = request.MovieId,
            EpisodeIds = [.. episodes.Select(e => e.Id)],
            ReleaseTitle = release.Title,
            Indexer = release.Indexer,
            State = TrackedDownloadState.Downloading,
        };
        db.TrackedDownloads.Add(download);

        QualityModel quality = QualityParser.ParseQuality(release.Title);
        IReadOnlyList<Language> languages = LanguageParser.ParseLanguages(release.Title);
        foreach (int? episodeId in episodes.Count == 0 ? [null] : episodes.Select(e => (int?)e.Id))
        {
            HistoryItem history = new()
            {
                EventType = HistoryEventType.Grabbed,
                SeriesId = request.SeriesId,
                EpisodeId = episodeId,
                MovieId = request.MovieId,
                SourceTitle = release.Title,
                Quality = quality,
                Languages = languages,
                InfoHash = id,
                Date = DateTime.UtcNow,
            };
            history.Data["indexer"] = release.Indexer;
            history.Data["size"] = release.Size?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
            history.Data["downloadUrl"] = release.MagnetUrl ?? release.DownloadUrl ?? "";
            db.History.Add(history);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // A concurrent grab of the same torrent inserted it first (InfoHash is unique).
            db.ChangeTracker.Clear();
            TrackedDownload? raced = await db.TrackedDownloads.FirstOrDefaultAsync(d => d.InfoHash == id, cancellationToken).ConfigureAwait(false);
            if (raced is null)
            {
                throw;
            }

            return raced;
        }

        return download;
    }

    // Magnet first; otherwise the definition's download block resolves the details page to a .torrent URL or a magnet.
    private async Task<string> AddTorrentAsync(ReleaseInfo release, string saveDirectory, string category, CancellationToken cancellationToken)
    {
        Uri link = release.MagnetUrl is string magnet ? new Uri(magnet)
            : release.DownloadUrl is string downloadUrl ? new Uri(downloadUrl)
            : throw new ArgumentException($"Release '{release.Title}' has no download link.", nameof(release));

        if (!IsMagnet(link))
        {
            // The release comes from the request body: only fetch a link on the site of the enabled indexer it names (no SSRF).
            IndexerCatalogEntry entry = catalog.Catalog.Find(release.IndexerId ?? "") is { Enabled: true } found ? found
                : throw new ArgumentException($"Release '{release.Title}' does not come from an enabled indexer.", nameof(release));
            if (!IsIndexerLink(link, [.. entry.Links, health.ActiveBaseUrl(entry.Definition)]))
            {
                throw new ArgumentException($"Release '{release.Title}' links to {link.Host}, which is not a site of {entry.Name}.", nameof(release));
            }

            CardigannDownloadResult resolved = await searcher.ResolveDownloadAsync(entry.Definition, health.ActiveBaseUrl(entry.Definition), link, cancellationToken).ConfigureAwait(false);
            link = resolved switch
            {
                CardigannDownloadResult.Resolved ok => ok.Link,
                CardigannDownloadResult.Failed failed => throw new InvalidOperationException($"{entry.Name}: {failed.Failure.Message}"),
                _ => throw new InvalidOperationException($"Unknown download result {resolved}."),
            };
        }

        if (IsMagnet(link))
        {
            return await torrents.Engine.AddMagnetAsync(link.OriginalString, saveDirectory, category).ConfigureAwait(false);
        }

        HttpClient http = httpClientFactory.CreateClient(nameof(DownloadService));
        using HttpResponseMessage response = await http.GetAsync(link, cancellationToken).ConfigureAwait(false);

        // The handler follows http redirects only; many sites redirect their .torrent link to a magnet.
        if (response.StatusCode is >= HttpStatusCode.MovedPermanently and < HttpStatusCode.BadRequest && response.Headers.Location is Uri location && IsMagnet(location))
        {
            return await torrents.Engine.AddMagnetAsync(location.OriginalString, saveDirectory, category).ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Downloading the torrent file failed: HTTP {(int)response.StatusCode} from {link.Host}.");
        }

        byte[] torrentFile = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return await torrents.Engine.AddTorrentFileAsync(torrentFile, saveDirectory, category).ConfigureAwait(false);
    }

    // http(s) on one of the indexer's hosts or a subdomain of one ("www." aside).
    public static bool IsIndexerLink(Uri link, IEnumerable<string> siteUrls)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(siteUrls);
        string host = TrimWww(link.Host);
        return (link.Scheme == Uri.UriSchemeHttp || link.Scheme == Uri.UriSchemeHttps)
            && siteUrls.Any(url => Uri.TryCreate(url, UriKind.Absolute, out Uri? site)
                && (host.Equals(TrimWww(site.Host), StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + TrimWww(site.Host), StringComparison.OrdinalIgnoreCase)));
    }

    private static string TrimWww(string host) => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

    private static bool IsMagnet(Uri link) => link.Scheme.Equals("magnet", StringComparison.OrdinalIgnoreCase);
}
