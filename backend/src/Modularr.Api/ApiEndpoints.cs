using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Discovery;
using Modularr.Core.Downloads;
using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Catalog;
using Modularr.Core.Languages;
using Modularr.Core.Library;
using Modularr.Core.Metadata;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;
using Modularr.Core.Search;
using Modularr.Core.Subtitles;

namespace Modularr.Api;

// HTTP shape only: every endpoint hands over to a Core service and projects the result.
public static class ApiEndpoints
{
    // No profile: the category default; no version: the version policy; the search runs in the background unless SearchNow is false.
    public sealed record AddSeriesRequest(int TvdbId, int? QualityProfileId = null, ReleaseVersion? Version = null, bool SearchNow = true, SeriesMonitor Monitor = SeriesMonitor.All);

    public sealed record AddMovieRequest(int TmdbId, int? QualityProfileId = null, ReleaseVersion? Version = null, bool SearchNow = true);

    public sealed record ImportRequest(IReadOnlyList<ImportItem> Items);

    public static RouteGroupBuilder MapModularrApi(this RouteGroupBuilder api)
    {
        api.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
        api.MapGet("/config", (IOptions<ModularrOptions> options, HttpContext context) =>
            Results.Ok(IsAdmin(context) ? options.Value : options.Value.Redacted()));
        api.MapGet("/indexers", Indexers);
        api.MapGet("/search", Search);
        api.MapGet("/lookup", Lookup);
        api.MapGet("/discover/movies", (string? list, DiscoveryService discovery, CancellationToken ct) =>
            discovery.DiscoverMoviesAsync(ParseEnum(list, MovieList.Popular), ct));
        api.MapGet("/discover/series", (string? list, DiscoveryService discovery, CancellationToken ct) =>
            discovery.DiscoverSeriesAsync(ParseEnum(list, SeriesList.Popular), ct));
        api.MapGet("/discover/anime", (string? list, DiscoveryService discovery, CancellationToken ct) =>
            discovery.DiscoverAnimeAsync(ParseEnum(list, AnimeList.Trending), ct));
        api.MapGet("/series", async (LibraryService library, ItemDtos dtos, CancellationToken ct) => (await library.ListSeriesAsync(ct)).Select(dtos.Of));
        api.MapGet("/series/{id:int}", async (int id, LibraryService library, ItemDtos dtos, CancellationToken ct) => dtos.Detail(await library.GetSeriesAsync(id, ct)));
        api.MapPost("/series", async (AddSeriesRequest request, LibraryService library, ItemDtos dtos, CancellationToken ct) =>
            dtos.Of(await library.AddSeriesAsync(request.TvdbId, request.QualityProfileId, request.Version, request.Monitor, request.SearchNow, ct)));
        api.MapPatch("/series/{id:int}", async (int id, ItemUpdate update, LibraryService library, ItemDtos dtos, CancellationToken ct) =>
            dtos.Of(await library.UpdateSeriesAsync(id, update, ct)));
        api.MapPost("/series/{id:int}/search", async (int id, int? season, ModularrDbContext db, SearchRunner searches, CancellationToken ct) =>
        {
            await EnsureExistsAsync(db.Series.AnyAsync(s => s.Id == id, ct), $"Series {id} is not in the library.");
            searches.Enqueue(new SearchJob(id, MovieId: null, season));
            return Results.Accepted();
        });
        api.MapGet("/movies", async (LibraryService library, ItemDtos dtos, CancellationToken ct) => (await library.ListMoviesAsync(ct)).Select(m => dtos.Of(m.Movie, m.HasFile)));
        api.MapGet("/movies/{id:int}", async (int id, LibraryService library, ItemDtos dtos, CancellationToken ct) =>
        {
            (Movie movie, bool hasFile) = await library.GetMovieAsync(id, ct);
            return new { movie = dtos.Of(movie), hasFile };
        });
        api.MapPost("/movies", async (AddMovieRequest request, LibraryService library, ItemDtos dtos, CancellationToken ct) =>
            dtos.Of(await library.AddMovieAsync(request.TmdbId, request.QualityProfileId, request.Version, request.SearchNow, ct)));
        api.MapPatch("/movies/{id:int}", async (int id, ItemUpdate update, LibraryService library, ItemDtos dtos, CancellationToken ct) =>
            dtos.Of(await library.UpdateMovieAsync(id, update, ct)));
        api.MapPost("/movies/{id:int}/search", async (int id, ModularrDbContext db, SearchRunner searches, CancellationToken ct) =>
        {
            await EnsureExistsAsync(db.Movies.AnyAsync(m => m.Id == id, ct), $"Movie {id} is not in the library.");
            searches.Enqueue(new SearchJob(SeriesId: null, id));
            return Results.Accepted();
        });
        api.MapPost("/series/{id:int}/rescan", (int id, DiskScanService scans, CancellationToken ct) => scans.ScanSeriesAsync(id, record: true, ct));
        api.MapPost("/movies/{id:int}/rescan", (int id, DiskScanService scans, CancellationToken ct) => scans.ScanMovieAsync(id, record: true, ct));
        api.MapPost("/library/rescan", (DiskScanService scans, CancellationToken ct) => scans.ScanAllAsync(ct)).RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapGet("/library/import", (string? root, LibraryImportService imports, CancellationToken ct) =>
            imports.ProposeAsync(ParseEnum(root, LibraryRoot.Movies), ct)).RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapPost("/library/import", (ImportRequest request, LibraryImportService imports, CancellationToken ct) =>
            imports.ImportAsync(request.Items, ct)).RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapGet("/qualities", () => Quality.All.Select(q => new { q.Id, q.Name, q.Source, q.Resolution }));
        api.MapGet("/languages", () => Language.All.Where(l => l != Language.Unknown).Select(l => new { l.Id, l.Name }));
        api.MapGet("/versions", () => ReleaseVersion.All.Select(v => v.Name));
        api.MapGet("/profiles", (QualityProfileService profiles, CancellationToken ct) => profiles.ListAsync(ct));
        api.MapPost("/profiles", (QualityProfile profile, QualityProfileService profiles, CancellationToken ct) => profiles.CreateAsync(profile, ct))
            .RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapPut("/profiles/{id:int}", (int id, QualityProfile profile, QualityProfileService profiles, CancellationToken ct) => profiles.UpdateAsync(id, profile, ct))
            .RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapDelete("/profiles/{id:int}", async (int id, QualityProfileService profiles, CancellationToken ct) =>
        {
            await profiles.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapPost("/releases/grab", async (GrabRequest request, DownloadService downloads, CancellationToken ct) =>
            ToDto(await downloads.GrabAsync(request, ct)));
        api.MapGet("/downloads", async (DownloadService downloads, CancellationToken ct) => (await downloads.ListAsync(ct)).Select(ToDto));
        api.MapPost("/downloads/{id:int}/pause", async (int id, DownloadService downloads, CancellationToken ct) =>
        {
            await downloads.SetPausedAsync(id, paused: true, ct);
            return Results.NoContent();
        });
        api.MapPost("/downloads/{id:int}/resume", async (int id, DownloadService downloads, CancellationToken ct) =>
        {
            await downloads.SetPausedAsync(id, paused: false, ct);
            return Results.NoContent();
        });
        api.MapDelete("/downloads/{id:int}", async (int id, bool? deleteData, bool? blocklist, DownloadService downloads, CancellationToken ct) =>
        {
            await downloads.RemoveAsync(id, deleteData ?? false, blocklist ?? false, ct);
            return Results.NoContent();
        });
        api.MapDelete("/series/{id:int}", async (int id, bool? deleteFiles, LibraryService library, CancellationToken ct) =>
        {
            await library.DeleteSeriesAsync(id, deleteFiles ?? false, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapDelete("/movies/{id:int}", async (int id, bool? deleteFiles, LibraryService library, CancellationToken ct) =>
        {
            await library.DeleteMovieAsync(id, deleteFiles ?? false, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapGet("/settings/paths", (IOptionsMonitor<ModularrOptions> options) => options.CurrentValue.Paths);
        api.MapPut("/settings/paths", async (PathOptions paths, SettingsFile settings, IOptionsMonitor<ModularrOptions> options, CancellationToken ct) =>
        {
            await settings.SavePathsAsync(paths, ct);
            return options.CurrentValue.Paths;
        }).RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapGet("/settings/media", (IOptionsMonitor<ModularrOptions> options) => options.CurrentValue.Media);
        api.MapPut("/settings/media", async (MediaOptions media, QualityProfileService profiles, IOptionsMonitor<ModularrOptions> options, CancellationToken ct) =>
        {
            await profiles.SaveMediaAsync(media, ct);
            return options.CurrentValue.Media;
        }).RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapGet("/settings/subtitles", (IOptionsMonitor<ModularrOptions> options, HttpContext context) =>
            IsAdmin(context) ? options.CurrentValue.Subtitles : options.CurrentValue.Subtitles.Redacted());
        api.MapPut("/settings/subtitles", async (SubtitleOptions subtitles, SettingsFile settings, IOptionsMonitor<ModularrOptions> options, CancellationToken ct) =>
        {
            await settings.SaveSubtitlesAsync(subtitles, ct);
            return options.CurrentValue.Subtitles;
        }).RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapGet("/filesystem", (string? path) => DirectoryBrowser.Browse(path)).RequireAuthorization(AuthEndpoints.AdminPolicy);
        return api;
    }

    // Core reports bad input as ArgumentException and an unusable upstream answer as the typed exceptions below.
    public static IApplicationBuilder UseApiErrors(this IApplicationBuilder app) => app.UseExceptionHandler(handler => handler.Run(async context =>
    {
        Exception? error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        (int status, string message) = error switch
        {
            // ArgumentException.Message ends with " (Parameter 'name')", which is noise for the UI.
            ArgumentException argument => (StatusCodes.Status400BadRequest, argument.ParamName is null ? argument.Message : argument.Message.Replace($" (Parameter '{argument.ParamName}')", "", StringComparison.Ordinal)),
            FormatException or InvalidSearchTermException => (StatusCodes.Status400BadRequest, error.Message),
            MetadataNotFoundException or KeyNotFoundException => (StatusCodes.Status404NotFound, error.Message),
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
                languages = d.Candidate.Languages.Audio.Select(l => l.Name),
                subtitles = d.Candidate.Languages.Subtitles.Select(l => l.Name),
                releaseGroup = d.Candidate.ReleaseGroup,
                approved = d.Approved,
                temporarilyRejected = d.TemporarilyRejected,
                rejections = d.Rejections.Select(r => new { reason = r.Reason, message = r.Message, type = r.Type }),
            }),
        };
    }

    private static Task<IReadOnlyList<DiscoveryItem>> Lookup(DiscoveryService discovery, string term, string? type, CancellationToken ct) =>
        type?.ToUpperInvariant() switch
        {
            null or "ALL" => discovery.LookupAsync(term, includeMovies: true, includeSeries: true, ct),
            "SERIES" => discovery.LookupAsync(term, includeMovies: false, includeSeries: true, ct),
            "MOVIE" => discovery.LookupAsync(term, includeMovies: true, includeSeries: false, ct),
            _ => throw new ArgumentException($"Unknown type '{type}', expected movie, series or all."),
        };

    private static bool IsAdmin(HttpContext context) => context.User.IsInRole(nameof(UserRole.Admin));

    private static async Task EnsureExistsAsync(Task<bool> exists, string message)
    {
        if (!await exists)
        {
            throw new KeyNotFoundException(message);
        }
    }

    // Library items as the UI sees them: version and subtitles are the item's own overrides (null follows the policy),
    // wantedVersion/wantedSubtitles the effective ones, policyVersion what the policy alone picks, lastSearch the latest
    // background search since startup.
    public sealed class ItemDtos(IOptionsMonitor<ModularrOptions> options, SearchRunner searches)
    {
        public object Of(Series s) => new
        {
            s.Id, s.TvdbId, s.Title, s.Year, s.SeriesType, s.Runtime, s.Monitored, s.Path, s.QualityProfileId, s.Added, s.Poster, s.Backdrop, s.Overview,
            s.Rating, s.Genres, s.Certification, s.OriginalLanguage, s.Version, s.Subtitles,
            wantedVersion = options.CurrentValue.Media.WantedVersion(s),
            policyVersion = options.CurrentValue.Media.WantedVersion(s.MediaType, s.OriginalLanguage, itemVersion: null),
            wantedSubtitles = options.CurrentValue.Media.WantedSubtitles(s),
            lastSearch = searches.LastSearch(s.Id, movieId: null),
            seasons = s.Seasons,
            episodes = s.Episodes.Count,
            episodesWithFile = s.Episodes.Count(e => e.MediaFileId is not null),
        };

        // HasFile only where the caller knows it (the movie list); null elsewhere.
        public object Of(Movie m, bool? hasFile = null) => new
        {
            m.Id, m.TmdbId, m.ImdbId, m.Title, m.Year, m.Runtime, m.Monitored, m.Path, m.QualityProfileId, m.Poster, m.Backdrop, m.Overview,
            m.Rating, m.Genres, m.Certification, m.OriginalLanguage, m.Version, m.Subtitles,
            wantedVersion = options.CurrentValue.Media.WantedVersion(m),
            policyVersion = options.CurrentValue.Media.WantedVersion(MediaType.Movie, m.OriginalLanguage, itemVersion: null),
            wantedSubtitles = options.CurrentValue.Media.WantedSubtitles(m),
            lastSearch = searches.LastSearch(seriesId: null, m.Id),
            hasFile,
        };

        public object Detail(Series s) => new
        {
            series = Of(s),
            seasons = s.Seasons.OrderBy(season => season.Number).Select(season => new
            {
                season.Number, season.Monitored,
                episodes = s.Episodes.Count(e => e.SeasonNumber == season.Number),
                episodesWithFile = s.Episodes.Count(e => e.SeasonNumber == season.Number && e.MediaFileId is not null),
            }),
            episodes = s.Episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).Select(e => new
            {
                e.Id, e.SeasonNumber, e.EpisodeNumber, e.AbsoluteEpisodeNumber, e.AirDate, e.Title, e.Monitored, hasFile = e.MediaFileId is not null,
            }),
        };
    }

    private static T ParseEnum<T>(string? value, T fallback)
        where T : struct, Enum =>
        value is null ? fallback
        : Enum.TryParse(value, ignoreCase: true, out T parsed) && Enum.IsDefined(parsed) ? parsed
        : throw new ArgumentException($"Unknown value '{value}', expected {string.Join(", ", Enum.GetNames<T>()).ToLowerInvariant()}.");

    private static object ToDto(TrackedDownload d) => new { d.Id, d.InfoHash, d.ReleaseTitle, d.Indexer, d.State, d.Message, d.SeriesId, d.MovieId, d.EpisodeIds, d.Regrabs };

    private static object ToDto(DownloadItem item) => new
    {
        item.Download.Id, item.Download.InfoHash, item.Download.ReleaseTitle, item.Download.Indexer, item.Download.State,
        item.Download.SeriesId, item.Download.MovieId, item.Download.EpisodeIds, item.Download.Regrabs, item.Download.Added,
        category = item.Download.MovieId is null ? DownloadService.SeriesCategory : DownloadService.MoviesCategory,
        eta = item.Torrent?.RemainingTime is TimeSpan eta ? (long?)eta.TotalSeconds : null,
        status = item.Torrent?.Status,
        // A failed download's torrent is removed, so its reason is the one kept on the download.
        message = item.Torrent?.Message ?? item.Download.Message,
        progress = item.Torrent?.Progress,
        size = item.Torrent?.TotalSize,
        remaining = item.Torrent?.RemainingSize,
        downloadRate = item.Torrent?.DownloadRate,
        uploadRate = item.Torrent?.UploadRate,
        ratio = item.Torrent?.SeedRatio,
        path = item.Torrent?.OutputPath,
    };
}
