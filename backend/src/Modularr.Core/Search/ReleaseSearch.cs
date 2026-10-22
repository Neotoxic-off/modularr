using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Cardigann;
using Modularr.Core.Indexers.Catalog;
using Modularr.Core.Languages;
using Modularr.Core.Persistence;

namespace Modularr.Core.Search;

public sealed record ReleaseSearchRequest
{
    // Free text; defaults to the library title when the series or movie is known.
    public string? Term { get; init; }

    // Series or Movie (anime is a series type).
    public MediaType Type { get; init; } = MediaType.Series;
    public int? Season { get; init; }
    public string? Episode { get; init; }
    public int? TvdbId { get; init; }
    public string? ImdbId { get; init; }
    public int? TmdbId { get; init; }

    public static ReleaseSearchRequest ForSeason(int tvdbId, int season) => new() { TvdbId = tvdbId, Season = season };

    public static ReleaseSearchRequest ForEpisode(int tvdbId, Episode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return new() { TvdbId = tvdbId, Season = episode.SeasonNumber, Episode = episode.EpisodeNumber.ToString(CultureInfo.InvariantCulture) };
    }
}

public sealed record ReleaseSearchResult(IReadOnlyList<DownloadDecision> Decisions, IReadOnlyList<IndexerError> Errors, int IndexersSearched, int? SeriesId, int? MovieId);

// Interactive search: every enabled indexer, every release parsed and judged against the series' or movie's profile (or the
// first profile when it is not in the library) and its wanted language (any when not in the library), approved releases first, each
// group best first.
public sealed class ReleaseSearch(ModularrDbContext db, IndexerCatalogHost catalog, IndexerSearch indexers, IOptionsMonitor<ModularrOptions> options)
{
    private const int TvCategory = 5000;
    private const int MoviesCategory = 2000;

    public async Task<ReleaseSearchResult> SearchAsync(ReleaseSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        bool movies = request.Type == MediaType.Movie;
        Series? series = !movies && request.TvdbId is int tvdbId
            ? await db.Series.AsNoTracking().Include(s => s.QualityProfile).Include(s => s.Episodes).FirstOrDefaultAsync(s => s.TvdbId == tvdbId, cancellationToken).ConfigureAwait(false)
            : null;
        Movie? movie = movies && request.TmdbId is int tmdbId
            ? await db.Movies.AsNoTracking().Include(m => m.QualityProfile).FirstOrDefaultAsync(m => m.TmdbId == tmdbId, cancellationToken).ConfigureAwait(false)
            : null;

        string term = request.Term ?? series?.Title ?? movie?.Title ?? "";
        if (string.IsNullOrWhiteSpace(term))
        {
            // An id-only query would be an RSS listing on every indexer without id search.
            throw new ArgumentException("A search term is required unless the series or movie is in the library.", nameof(request));
        }

        CardigannSearchQuery query = new()
        {
            Type = movies ? CardigannSearchType.MovieSearch : CardigannSearchType.TvSearch,
            Term = term,
            Categories = [movies ? MoviesCategory : TvCategory],
            Season = request.Season,
            Episode = request.Episode,
            TvdbId = movies ? null : request.TvdbId,
            TmdbId = movies ? request.TmdbId : null,
            ImdbId = ParseImdbId(request.ImdbId ?? movie?.ImdbId),
        };

        // Like Prowlarr, only indexers whose categories cover the search (a TV-only site is not asked for a movie).
        string root = movies ? "Movies" : "TV";
        Task<IndexerSearchResult> searching = indexers.SearchAsync(
            catalog.Catalog.Entries.Where(e => e.Enabled && e.Categories.Any(c => IndexerCatalog.IsCategoryOf(c, root))).Select(e => e.Definition), query, cancellationToken);

        QualityProfile profile = series?.QualityProfile ?? movie?.QualityProfile
            ?? await db.QualityProfiles.AsNoTracking().OrderBy(p => p.Id).FirstAsync(cancellationToken).ConfigureAwait(false);
        int? seriesId = series?.Id;
        int? movieId = movie?.Id;
        List<MediaFile> files = [];
        List<BlocklistEntry> blocklist = [];
        if (seriesId is not null || movieId is not null)
        {
            files = await db.MediaFiles.AsNoTracking().Where(f => f.SeriesId == seriesId && f.MovieId == movieId).ToListAsync(cancellationToken).ConfigureAwait(false);
            blocklist = await db.Blocklist.AsNoTracking().Where(b => b.SeriesId == seriesId && b.MovieId == movieId)
                .Select(b => new BlocklistEntry(b.SourceTitle, b.InfoHash, b.Indexer)).ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        IndexerSearchResult found = await searching.ConfigureAwait(false);
        // A daily episode ("2024-01-31") is only shaped into the query, not matched.
        int? episodeNumber = int.TryParse(request.Episode, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : null;
        ModularrOptions settings = options.CurrentValue;
        Language wanted = series is not null ? settings.Media.WantedLanguage(series)
            : movie is not null ? settings.Media.WantedLanguage(movie)
            : Language.Any;
        DecisionContext context = new() { Options = settings.Decisions, Blocklist = blocklist, WantedLanguage = wanted };
        Dictionary<int, MediaFile> filesById = files.ToDictionary(f => f.Id);
        DownloadDecision[] decisions =
        [
            .. found.Releases.Select(release => movies
                ? ReleaseDecisions.ForMovie(release, profile, movie, files, context)
                : ReleaseDecisions.ForSeries(release, profile, series, filesById, context, request.Season, episodeNumber)),
        ];

        return new ReleaseSearchResult(
            [.. DownloadDecisionMaker.Prioritize(decisions.Where(d => d.Approved), context), .. DownloadDecisionMaker.Prioritize(decisions.Where(d => !d.Approved), context)],
            found.Errors,
            found.IndexersSearched,
            seriesId,
            movieId);
    }

    // "tt0111161" or "0111161"; anything else is ignored rather than failing the search.
    private static int? ParseImdbId(string? imdbId) =>
        int.TryParse(imdbId?.Trim().TrimStart('t', 'T'), NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0 ? id : null;
}
