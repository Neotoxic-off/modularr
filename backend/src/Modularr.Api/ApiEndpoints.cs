using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Downloads;
using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Catalog;
using Modularr.Core.Library;
using Modularr.Core.Metadata;
using Modularr.Core.Persistence;
using Modularr.Core.Search;

namespace Modularr.Api;

// HTTP shape only: every endpoint hands over to a Core service and projects the result.
public static class ApiEndpoints
{
    public sealed record AddSeriesRequest(int TvdbId, int QualityProfileId = 1);

    public sealed record AddMovieRequest(int TmdbId, int QualityProfileId = 1);

    public static RouteGroupBuilder MapModularrApi(this RouteGroupBuilder api)
    {
        api.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        api.MapGet("/config", (IOptions<ModularrOptions> options) => Results.Ok(options.Value));
        api.MapGet("/indexers", Indexers);
        api.MapGet("/search", Search);
        api.MapGet("/lookup", Lookup);
        api.MapGet("/series", async (LibraryService library, CancellationToken ct) => (await library.ListSeriesAsync(ct)).Select(ToDto));
        api.MapPost("/series", async (AddSeriesRequest request, LibraryService library, CancellationToken ct) =>
            ToDto(await library.AddSeriesAsync(request.TvdbId, request.QualityProfileId, ct)));
        api.MapGet("/movies", async (LibraryService library, CancellationToken ct) => (await library.ListMoviesAsync(ct)).Select(ToDto));
        api.MapPost("/movies", async (AddMovieRequest request, LibraryService library, CancellationToken ct) =>
            ToDto(await library.AddMovieAsync(request.TmdbId, request.QualityProfileId, ct)));
        api.MapPost("/releases/grab", async (GrabRequest request, DownloadService downloads, CancellationToken ct) =>
            ToDto(await downloads.GrabAsync(request, ct)));
        api.MapGet("/downloads", async (DownloadService downloads, CancellationToken ct) => (await downloads.ListAsync(ct)).Select(ToDto));
        return api;
    }

    // Core reports bad input as ArgumentException and an unusable upstream answer as the typed exceptions below.
    public static IApplicationBuilder UseApiErrors(this IApplicationBuilder app) => app.UseExceptionHandler(handler => handler.Run(async context =>
    {
        Exception? error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        (int status, string message) = error switch
        {
            ArgumentException or FormatException or InvalidSearchTermException => (StatusCodes.Status400BadRequest, error.Message),
            MetadataNotFoundException => (StatusCodes.Status404NotFound, error.Message),
            MetadataSourceException or InvalidOperationException or HttpRequestException => (StatusCodes.Status502BadGateway, error.Message),
            _ => (StatusCodes.Status500InternalServerError, "Internal error"),
        };
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error = message });
    }));

    private static object Indexers(IndexerCatalogHost host, IndexerDomainHealth health)
    {
        IndexerCatalog catalog = host.Catalog;
        return new
        {
            total = catalog.Entries.Count,
            enabled = catalog.Entries.Count(e => e.Enabled),
            lastSyncError = host.LastSyncError,
            loadErrors = catalog.Errors,
            indexers = catalog.Entries.Select(e => new
            {
                id = e.Id,
                name = e.Name,
                language = e.Language,
                privacy = e.Privacy,
                enabled = e.Enabled,
                health = health.Health(e.Definition),
            }),
        };
    }

    private static async Task<object> Search(
        ReleaseSearch search, string? q, string? type, int? season, string? episode, int? tvdbId, string? imdbId, int? tmdbId, CancellationToken ct)
    {
        MediaType mediaType = type?.ToUpperInvariant() switch
        {
            null or "SERIES" => MediaType.Series,
            "MOVIE" => MediaType.Movie,
            _ => throw new ArgumentException($"Unknown type '{type}', expected series or movie."),
        };

        ReleaseSearchResult result = await search.SearchAsync(
            new ReleaseSearchRequest { Term = q, Type = mediaType, Season = season, Episode = episode, TvdbId = tvdbId, ImdbId = imdbId, TmdbId = tmdbId }, ct);

        return new
        {
            indexersSearched = result.IndexersSearched,
            releases = result.Decisions.Count,
            approved = result.Decisions.Count(d => d.Approved),
            seriesId = result.SeriesId,
            movieId = result.MovieId,
            errors = result.Errors,
            decisions = result.Decisions.Select(d => new
            {
                release = d.Candidate.Release,
                quality = d.Candidate.Quality.ToString(),
                languages = d.Candidate.Languages.Select(l => l.Name),
                releaseGroup = d.Candidate.ReleaseGroup,
                approved = d.Approved,
                temporarilyRejected = d.TemporarilyRejected,
                rejections = d.Rejections.Select(r => new { reason = r.Reason, message = r.Message, type = r.Type }),
            }),
        };
    }

    private static async Task<object> Lookup(SeriesMetadataClient series, MovieMetadataClient movies, string term, string? type, CancellationToken ct) =>
        type?.ToUpperInvariant() switch
        {
            null or "SERIES" => (await series.SearchSeriesAsync(term, cancellationToken: ct)).Select(s => new { s.TvdbId, s.ImdbId, s.Title, s.Year, s.Overview, s.Status, s.Images }),
            "MOVIE" => (await movies.SearchMoviesAsync(term, ct)).Select(m => new { m.TmdbId, m.ImdbId, m.Title, m.Year, m.Overview, m.Status, m.Images }),
            _ => throw new ArgumentException($"Unknown type '{type}', expected series or movie."),
        };

    private static object ToDto(Series s) => new
    {
        s.Id, s.TvdbId, s.Title, s.Year, s.SeriesType, s.Runtime, s.Monitored, s.Path, s.QualityProfileId, s.Added,
        seasons = s.Seasons,
        episodes = s.Episodes.Count,
        episodesWithFile = s.Episodes.Count(e => e.MediaFileId is not null),
    };

    private static object ToDto(Movie m) => new { m.Id, m.TmdbId, m.ImdbId, m.Title, m.Year, m.Runtime, m.Monitored, m.Path, m.QualityProfileId };

    private static object ToDto(TrackedDownload d) => new { d.Id, d.InfoHash, d.ReleaseTitle, d.Indexer, d.State, d.SeriesId, d.MovieId, d.EpisodeIds };

    private static object ToDto(DownloadItem item) => new
    {
        item.Download.Id, item.Download.InfoHash, item.Download.ReleaseTitle, item.Download.Indexer, item.Download.State,
        item.Download.SeriesId, item.Download.MovieId, item.Download.EpisodeIds,
        status = item.Torrent?.Status,
        message = item.Torrent?.Message,
        progress = item.Torrent?.Progress,
        size = item.Torrent?.TotalSize,
        remaining = item.Torrent?.RemainingSize,
        downloadRate = item.Torrent?.DownloadRate,
        uploadRate = item.Torrent?.UploadRate,
        ratio = item.Torrent?.SeedRatio,
        path = item.Torrent?.OutputPath,
    };
}
