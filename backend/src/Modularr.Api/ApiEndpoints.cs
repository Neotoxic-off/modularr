using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Accounts;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Discovery;
using Modularr.Core.Downloads;
using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Cardigann;
using Modularr.Core.Indexers.Catalog;
using Modularr.Core.Languages;
using Modularr.Core.Library;
using Modularr.Core.Downloads.Torrents;
using Modularr.Core.Metadata;
using Modularr.Core.Notifications;
using Modularr.Core.Persistence;
using Modularr.Core.PostProcessing;
using Modularr.Core.Qualities;
using Modularr.Core.Search;
using Modularr.Core.Subtitles;

namespace Modularr.Api;

// HTTP shape only: every endpoint hands over to a Core service and projects the result.
public static class ApiEndpoints
{
    // No profile: the category default; no version: the version policy; the search runs in the background unless SearchNow is false.
    public sealed record AddSeriesRequest(int TvdbId, int? QualityProfileId = null, ReleaseVersion? Version = null, bool SearchNow = true, SeriesMonitor Monitor = SeriesMonitor.All, IReadOnlyList<int>? Seasons = null,
        IReadOnlyList<EpisodeKey>? Episodes = null, IReadOnlyList<Language>? Subtitles = null);

    // Subtitles: the title's own wanted subtitle languages; none follows the category default.
    public sealed record AddMovieRequest(int TmdbId, int? QualityProfileId = null, ReleaseVersion? Version = null, bool SearchNow = true, IReadOnlyList<Language>? Subtitles = null);

    public sealed record ImportRequest(IReadOnlyList<ImportItem> Items);

    public sealed record MonitoredRequest(bool Monitored);

    public sealed record EpisodesMonitoredRequest(IReadOnlyList<int> EpisodeIds, bool Monitored);

    public sealed record ImportModeRequest(ImportMode ImportMode, bool RenameFiles, string? RecycleBinPath);

    public sealed record MetadataRequest(string? TmdbApiKey);

    public static RouteGroupBuilder MapModularrApi(this RouteGroupBuilder api)
    {
        api.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
        api.MapGet("/config", (IOptions<ModularrOptions> options, HttpContext context) =>
            Results.Ok(IsAdmin(context) ? options.Value : options.Value.Redacted()));
        api.MapGet("/indexers", Indexers);
        api.MapPost("/indexers/{id}/test", TestIndexer);
        api.MapGet("/indexers/{id}/settings", (string id, IndexerCatalogHost host, IOptionsMonitor<ModularrOptions> options) =>
            IndexerSettings(FindIndexer(host, id), options.CurrentValue)).RequireAuthorization(AuthEndpoints.AdminPolicy);
        // An empty secret keeps the stored one; the saved login takes effect at once (new session, indexer enabled when complete).
        api.MapPut("/indexers/{id}/settings", async (string id, Dictionary<string, string?> values, IndexerCatalogHost host, SettingsFile settings, CardigannSessions sessions,
            IOptionsMonitor<ModularrOptions> options, CancellationToken ct) =>
        {
            IndexerCatalogEntry entry = FindIndexer(host, id);
            IReadOnlyDictionary<string, string> merged = CardigannSettings.Merge(entry.Definition, options.CurrentValue.Indexers.Settings.GetValueOrDefault(entry.Id), values);
            await settings.SaveIndexerSettingsAsync(entry.Id, merged, ct);
            sessions.Clear(entry.Id);
            host.Reload();

            // A test failed with the old settings says nothing about the new ones.
            host.Report(entry.Id, works: true);
            return IndexerSettings(FindIndexer(host, id), options.CurrentValue);
        }).RequireAuthorization(AuthEndpoints.AdminPolicy);
        // Each of these makes the server call indexers or metadata providers (the operator's quota): a per-user limit.
        api.MapGet("/search", Search).RequireRateLimiting(AuthEndpoints.OutboundRateLimit);
        api.MapGet("/lookup", Lookup).RequireRateLimiting(AuthEndpoints.OutboundRateLimit);
        // The regular seasons of a series not added yet, to pick which ones to monitor.
        api.MapGet("/lookup/seasons", async (int tvdbId, SeriesMetadataClient metadata, CancellationToken ct) =>
        {
            SeriesInfo info = await metadata.GetSeriesAsync(tvdbId, cancellationToken: ct);
            return info.Seasons.Where(s => s.SeasonNumber > 0).OrderBy(s => s.SeasonNumber).Select(s => new
            {
                number = s.SeasonNumber,
                episodes = info.Episodes.Where(e => e.SeasonNumber == s.SeasonNumber).DistinctBy(e => e.EpisodeNumber).OrderBy(e => e.EpisodeNumber)
                    .Select(e => new { number = e.EpisodeNumber, e.Title, e.AirDate }),
            });
        }).RequireRateLimiting(AuthEndpoints.OutboundRateLimit);
        api.MapGet("/discover/movies", (string? list, DiscoveryService discovery, CancellationToken ct) =>
            discovery.DiscoverMoviesAsync(ParseEnum(list, MovieList.Popular), ct)).RequireRateLimiting(AuthEndpoints.OutboundRateLimit);
        api.MapGet("/discover/series", (string? list, DiscoveryService discovery, CancellationToken ct) =>
            discovery.DiscoverSeriesAsync(ParseEnum(list, SeriesList.Popular), ct)).RequireRateLimiting(AuthEndpoints.OutboundRateLimit);
        api.MapGet("/discover/anime", (string? list, DiscoveryService discovery, CancellationToken ct) =>
            discovery.DiscoverAnimeAsync(ParseEnum(list, AnimeList.Trending), ct)).RequireRateLimiting(AuthEndpoints.OutboundRateLimit);
        api.MapGet("/series", async (LibraryService library, ItemDtos dtos, CancellationToken ct) => (await library.ListSeriesAsync(ct)).Select(dtos.Of));
        api.MapGet("/series/{id:int}", async (int id, LibraryService library, ItemDtos dtos, CancellationToken ct) => dtos.Detail(await library.GetSeriesAsync(id, ct)));
        api.MapPost("/series", async (AddSeriesRequest request, LibraryService library, ItemDtos dtos, CancellationToken ct) =>
            dtos.Of(await library.AddSeriesAsync(request.TvdbId, request.QualityProfileId, request.Version, request.Monitor, request.Seasons, request.SearchNow, ct, request.Episodes, request.Subtitles)));
        api.MapPatch("/series/{id:int}", async (int id, ItemUpdate update, LibraryService library, ItemDtos dtos, CancellationToken ct) =>
            dtos.Of(await library.UpdateSeriesAsync(id, update, ct)));
        api.MapPatch("/series/{id:int}/seasons/{number:int}", async (int id, int number, MonitoredRequest request, LibraryService library, ItemDtos dtos, CancellationToken ct) =>
            dtos.Detail(await library.SetSeasonMonitoredAsync(id, number, request.Monitored, ct)));
        api.MapPatch("/episodes/{id:int}", async (int id, MonitoredRequest request, LibraryService library, CancellationToken ct) =>
            ItemDtos.Of((await library.SetEpisodesMonitoredAsync([id], request.Monitored, ct))[0]));
        api.MapPatch("/episodes", async (EpisodesMonitoredRequest request, LibraryService library, CancellationToken ct) =>
            (await library.SetEpisodesMonitoredAsync(request.EpisodeIds ?? [], request.Monitored, ct)).Select(ItemDtos.Of));

        // 409 when every monitored episode (of the season) has a file, unless upgrade is asked and one is below the cutoff.
        api.MapPost("/series/{id:int}/search", async (int id, int? season, bool? upgrade, ModularrDbContext db, SearchRunner searches, CancellationToken ct) =>
        {
            await PresentFiles.EnsureEpisodesWantedAsync(
                db, id, e => e.Monitored && (season == null || e.SeasonNumber == season),
                season is null ? "Every monitored episode" : $"Every monitored episode of season {season}", upgrade ?? false, ct);
            searches.Enqueue(new SearchJob(id, MovieId: null, season));
            return Results.Accepted();
        });
        api.MapGet("/movies", async (LibraryService library, ItemDtos dtos, CancellationToken ct) => (await library.ListMoviesAsync(ct)).Select(m => dtos.Of(m.Movie, m.HasFile)));
        api.MapGet("/movies/{id:int}", async (int id, LibraryService library, ItemDtos dtos, CancellationToken ct) =>
        {
            (Movie movie, MediaFile? file) = await library.GetMovieAsync(id, ct);
            return new
            {
                movie = dtos.Of(movie, file is not null),
                hasFile = file is not null,
                file = file is null ? null : new
                {
                    quality = file.Quality.Quality.Name,
                    version = ReleaseVersion.Best(new ReleaseLanguages(file.Languages), movie.OriginalLanguage)?.Name,
                },
            };
        });
        api.MapPost("/movies", async (AddMovieRequest request, LibraryService library, ItemDtos dtos, CancellationToken ct) =>
            await MovieAsync((await library.AddMovieAsync(request.TmdbId, request.QualityProfileId, request.Version, request.SearchNow, ct, request.Subtitles)).Id, library, dtos, ct));
        api.MapPatch("/movies/{id:int}", async (int id, ItemUpdate update, LibraryService library, ItemDtos dtos, CancellationToken ct) =>
        {
            await library.UpdateMovieAsync(id, update, ct);
            return await MovieAsync(id, library, dtos, ct);
        });

        // 409 when the movie has a file, unless upgrade is asked and the file is below the cutoff.
        api.MapPost("/movies/{id:int}/search", async (int id, bool? upgrade, ModularrDbContext db, SearchRunner searches, CancellationToken ct) =>
        {
            await PresentFiles.EnsureMovieWantedAsync(db, id, upgrade ?? false, ct);
            searches.Enqueue(new SearchJob(SeriesId: null, id));
            return Results.Accepted();
        });
        api.MapGet("/explore/{kind}", (string kind, int? page, string? genre, int? year, int? yearFrom, int? yearTo, decimal? minRating, string? language,
            string? status, string? sort, DiscoveryService discovery, CancellationToken ct) =>
            discovery.ExploreAsync(ExploreKind(kind), new ExploreQuery
            {
                Page = page ?? 1,
                Genre = string.IsNullOrWhiteSpace(genre) ? null : genre.Trim(),
                Year = year,
                YearFrom = yearFrom,
                YearTo = yearTo,
                MinRating = minRating,
                Language = string.IsNullOrWhiteSpace(language) ? null : Language.FindByName(language),
                Status = status is null ? null : ParseEnum(status, ExploreStatus.Released),
                Sort = ParseEnum(sort, ExploreSort.Popularity),
            }, ct)).RequireRateLimiting(AuthEndpoints.OutboundRateLimit);
        api.MapGet("/explore/{kind}/genres", (string kind, DiscoveryService discovery, CancellationToken ct) => discovery.ExploreGenresAsync(ExploreKind(kind), ct))
            .RequireRateLimiting(AuthEndpoints.OutboundRateLimit);
        api.MapGet("/dashboard", async (DashboardService dashboard, DownloadService downloads, CancellationToken ct) =>
        {
            DashboardSummary summary = await dashboard.GetAsync(DateOnly.FromDateTime(DateTime.Now), ct);
            return new
            {
                summary.Counts,
                summary.Storage,
                summary.Subtitles,
                inProgress = (await downloads.ListAsync(inProgressOnly: true, ct)).Select(ToDto),
                summary.Airing,
                summary.Recent,
            };
        });
        api.MapGet("/calendar", (DateOnly start, DateOnly end, CalendarService calendar, CancellationToken ct) => calendar.GetAsync(start, end, ct));
        api.MapGet("/calendar/upcoming", (CalendarService calendar, CancellationToken ct) => calendar.UpcomingAsync(DateOnly.FromDateTime(DateTime.Now), ct));
        api.MapPost("/series/{id:int}/rescan", (int id, DiskScanService scans, CancellationToken ct) => scans.ScanSeriesAsync(id, record: true, ct));
        api.MapPost("/movies/{id:int}/rescan", (int id, DiskScanService scans, CancellationToken ct) => scans.ScanMovieAsync(id, record: true, ct));
        api.MapPost("/library/rescan", (DiskScanService scans, CancellationToken ct) => scans.ScanAllAsync(record: true, ct)).RequireAuthorization(AuthEndpoints.AdminPolicy);
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
        api.MapGet("/downloads", async (DownloadService downloads, CancellationToken ct) => (await downloads.ListAsync(inProgressOnly: false, ct)).Select(ToDto));
        // The public address torrent traffic leaves from (the VPN's when there is one) and the engine's listen port.
        api.MapGet("/downloads/network", (PublicIpMonitor ip, TorrentEngineHost torrents) => ToNetworkDto(ip.Last, torrents.Engine));
        api.MapPost("/downloads/network/refresh", async (PublicIpMonitor ip, TorrentEngineHost torrents, CancellationToken ct) =>
            ToNetworkDto(await ip.RefreshAsync(ct), torrents.Engine));
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
        api.MapPost("/downloads/{id:int}/retry", async (int id, DownloadService downloads, CancellationToken ct) =>
        {
            await downloads.RetryImportAsync(id, ct);
            return Results.NoContent();
        });
        api.MapDelete("/downloads/{id:int}", async (int id, bool? deleteData, bool? blocklist, DownloadService downloads, CancellationToken ct) =>
        {
            await downloads.RemoveAsync(id, deleteData ?? false, blocklist ?? false, ct);
            return Results.NoContent();
        });
        // Releases never grabbed again: removed by hand, failed, stalled or refused by the content check. Deleting one lifts the ban.
        api.MapGet("/blocklist", async (ModularrDbContext db, CancellationToken ct) =>
            (await db.Blocklist.AsNoTracking().OrderByDescending(b => b.Id).Take(500).ToListAsync(ct))
                .Select(b => new { b.Id, b.SeriesId, b.MovieId, Title = b.SourceTitle, Quality = b.Quality.Quality.Name, b.Indexer, b.Reason, b.Date }));
        api.MapDelete("/blocklist/{id:int}", async (int id, ModularrDbContext db, CancellationToken ct) =>
            await db.Blocklist.Where(b => b.Id == id).ExecuteDeleteAsync(ct) == 0 ? Results.NotFound() : Results.NoContent());
        api.MapDelete("/series/{id:int}", async (int id, bool? deleteFiles, LibraryService library, DownloadService downloads, CancellationToken ct) =>
        {
            // Torrents first: deleting the title cascades to its download rows, after which they cannot be found.
            await downloads.RemoveForTitleAsync(id, movieId: null, deleteFiles ?? false, ct);
            await library.DeleteSeriesAsync(id, deleteFiles ?? false, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapDelete("/movies/{id:int}", async (int id, bool? deleteFiles, LibraryService library, DownloadService downloads, CancellationToken ct) =>
        {
            await downloads.RemoveForTitleAsync(seriesId: null, id, deleteFiles ?? false, ct);
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
        api.MapGet("/settings/import", (IOptionsMonitor<ModularrOptions> options) => ImportSettings(options.CurrentValue.PostProcessing))
            .RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapPut("/settings/import", async (ImportModeRequest request, SettingsFile settings, IOptionsMonitor<ModularrOptions> options, CancellationToken ct) =>
        {
            await settings.SaveImportAsync(request.ImportMode, request.RenameFiles, request.RecycleBinPath, ct);
            return ImportSettings(options.CurrentValue.PostProcessing);
        }).RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapGet("/settings/automation", (IOptionsMonitor<ModularrOptions> options) => options.CurrentValue.Automation);
        api.MapPut("/settings/automation", async (AutomationOptions automation, SettingsFile settings, IOptionsMonitor<ModularrOptions> options, CancellationToken ct) =>
        {
            await settings.SaveAutomationAsync(automation, ct);
            return options.CurrentValue.Automation;
        }).RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapGet("/settings/metadata", (IOptionsMonitor<ModularrOptions> options) => MetadataSettings(options.CurrentValue.Metadata))
            .RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapPut("/settings/metadata", async (MetadataRequest request, SettingsFile settings, IOptionsMonitor<ModularrOptions> options, CancellationToken ct) =>
        {
            await settings.SaveTmdbApiKeyAsync(request.TmdbApiKey, ct);
            return MetadataSettings(options.CurrentValue.Metadata);
        }).RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapPost("/settings/metadata/test", async (TmdbClient tmdb, CancellationToken ct) =>
        {
            string? error = await tmdb.TestAsync(ct);
            return new { ok = error is null, error };
        }).RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapGet("/settings/notifications", (IOptionsMonitor<ModularrOptions> options) => options.CurrentValue.Notifications)
            .RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapPut("/settings/notifications", async (IReadOnlyList<NotificationConnection> notifications, SettingsFile settings, IOptionsMonitor<ModularrOptions> options, CancellationToken ct) =>
        {
            await settings.SaveNotificationsAsync(notifications, ct);
            return options.CurrentValue.Notifications;
        }).RequireAuthorization(AuthEndpoints.AdminPolicy);
        // Takes the connection as edited, so it can be tried before it is saved.
        api.MapPost("/settings/notifications/test", async (NotificationConnection connection, NotificationSender sender, CancellationToken ct) =>
        {
            connection.EnsureValid();
            string? error = await sender.SendAsync(connection, new NotificationMessage(NotificationEvent.Test, "Modularr", "Test notification", null, null, DateTime.UtcNow), ct);
            return new { ok = error is null, error };
        }).RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapGet("/settings/torrents", (IOptionsMonitor<ModularrOptions> options) => options.CurrentValue.Torrents)
            .RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapPut("/settings/torrents", async (TorrentEngineOptions torrents, SettingsFile settings, TorrentEngineHost engine, IOptionsMonitor<ModularrOptions> options, CancellationToken ct) =>
        {
            await settings.SaveTorrentsAsync(torrents, ct);
            await engine.Engine.ApplyAsync(options.CurrentValue.Torrents);
            return options.CurrentValue.Torrents;
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
            AlreadyPresentException => (StatusCodes.Status409Conflict, error.Message),
            MetadataSourceException or InvalidOperationException or HttpRequestException => (StatusCodes.Status502BadGateway, error.Message),
            _ => (StatusCodes.Status500InternalServerError, "Internal error"),
        };
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error = message });
    }));

    private static IndexerCatalogEntry FindIndexer(IndexerCatalogHost host, string id) =>
        host.Catalog.Find(id) ?? throw new KeyNotFoundException($"Unknown indexer '{id}'.");

    // The login form of an indexer, generated from its definition's settings; a secret's value is never sent back, only whether it is set.
    private static object IndexerSettings(IndexerCatalogEntry entry, ModularrOptions options)
    {
        IReadOnlyDictionary<string, string> values = options.Indexers.Settings.GetValueOrDefault(entry.Id) ?? new Dictionary<string, string>();
        return new
        {
            id = entry.Id,
            needsLogin = CardigannSettings.NeedsLogin(entry.Definition),
            configured = CardigannSettings.IsConfigured(entry.Definition, values),
            captcha = entry.Definition.Login?.Captcha is not null,
            fields = entry.Definition.Settings.Where(f => f.Type != "cardigannCaptcha").Select(f => new
            {
                name = f.Name,
                type = CardigannSettings.IsInfo(f) ? "info" : f.Type,
                label = f.Label ?? f.Name,
                @default = f.Default,
                options = f.Options?.Select(o => new { value = o.Key, label = o.Value }),
                secret = CardigannSettings.IsSecret(f),
                required = CardigannSettings.Required(entry.Definition).Contains(f),
                value = CardigannSettings.IsSecret(f) ? null : values.GetValueOrDefault(f.Name),
                set = values.ContainsKey(f.Name),
            }),
        };
    }

    private static object Indexers(IndexerCatalogHost host, IndexerDomainHealth health, IOptionsMonitor<ModularrOptions> options)
    {
        IndexerCatalog catalog = host.Catalog;
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> settings = options.CurrentValue.Indexers.Settings;
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
                failing = host.IsFailing(e.Id),
                needsLogin = CardigannSettings.NeedsLogin(e.Definition),
                configured = CardigannSettings.IsConfigured(e.Definition, settings.GetValueOrDefault(e.Id)),
                health = health.Health(e.Definition),
                description = e.Description,
                categories = e.Categories,
                // Every domain the definition knows (the configured override first), tried in this order when one fails.
                mirrors = health.Candidates(e.Definition),
            }),
        };
    }

    // Prowlarr's test: a search without a term (the latest releases), which also updates the indexer's health.
    private static async Task<IResult> TestIndexer(string id, IndexerCatalogHost host, IndexerSearch search, IndexerDomainHealth health, CancellationToken ct)
    {
        if (host.Catalog.Find(id) is not IndexerCatalogEntry entry)
        {
            return Results.NotFound(new { error = $"Unknown indexer '{id}'." });
        }

        IndexerSearchResult result = await search.SearchAsync([entry.Definition], new CardigannSearchQuery(), ct, includeCoolingDown: true);
        host.Report(entry.Id, result.Errors.Count == 0);
        return Results.Ok(new { releases = result.Releases.Count, error = result.Errors.Count > 0 ? result.Errors[0].Message : null, health = health.Health(entry.Definition) });
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
                version = ReleaseVersion.Best(d.Candidate.Languages, d.Candidate.OriginalLanguage)?.Name,
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

    // The settings key is shown to the admin who typed it; the effective key (possibly the environment's) only masked.
    private static object MetadataSettings(MetadataOptions metadata) => new
    {
        tmdbApiKey = metadata.TmdbApiKey,
        source = metadata.TmdbKeySource,
        effective = metadata.EffectiveTmdbApiKey is string key ? new string('•', 8) + key[^Math.Min(4, key.Length)..] : null,
    };

    private static object ImportSettings(PostProcessingOptions postProcessing) =>
        new { postProcessing.ImportMode, postProcessing.RenameFiles, postProcessing.RecycleBinPath };

    private static async Task<object> MovieAsync(int id, LibraryService library, ItemDtos dtos, CancellationToken ct)
    {
        (Movie movie, MediaFile? file) = await library.GetMovieAsync(id, ct);
        return dtos.Of(movie, file is not null);
    }

    private static MediaType ExploreKind(string kind) => kind.ToUpperInvariant() switch
    {
        "MOVIES" => MediaType.Movie,
        "SERIES" => MediaType.Series,
        "ANIME" => MediaType.Anime,
        _ => throw new ArgumentException($"Unknown kind '{kind}', expected movies, series or anime."),
    };

    // Library items as the UI sees them: version and subtitles are the item's own overrides (null follows the policy),
    // wantedVersion/wantedSubtitles the effective ones, policyVersion what the policy alone picks, lastSearch the latest
    // background search since startup. For a series and its seasons, hasFile means every monitored episode has a file (what a
    // search without upgrade refuses) and missing counts the monitored episodes aired without one.
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
            seasons = Seasons(s),
            episodes = s.Episodes.Count,
            episodesWithFile = s.Episodes.Count(e => e.MediaFileId is not null),
            missing = Missing(s.Episodes),
            hasFile = Complete(s.Episodes),
        };

        public object Of(Movie m, bool hasFile) => new
        {
            m.Id, m.TmdbId, m.ImdbId, m.Title, m.Year, m.Runtime, m.Monitored, m.Path, m.QualityProfileId, m.Poster, m.Backdrop, m.Overview,
            m.Rating, m.Genres, m.Certification, m.OriginalLanguage, m.Version, m.Subtitles, m.CollectionTmdbId, m.CollectionName,
            wantedVersion = options.CurrentValue.Media.WantedVersion(m),
            policyVersion = options.CurrentValue.Media.WantedVersion(MediaType.Movie, m.OriginalLanguage, itemVersion: null),
            wantedSubtitles = options.CurrentValue.Media.WantedSubtitles(m),
            lastSearch = searches.LastSearch(seriesId: null, m.Id),
            hasFile,
        };

        public object Detail(Series s) => new
        {
            series = Of(s),
            seasons = Seasons(s),
            episodes = s.Episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).Select(Of),
        };

        public static object Of(Episode e) => new
        {
            e.Id, e.SeriesId, e.SeasonNumber, e.EpisodeNumber, e.AbsoluteEpisodeNumber, e.AirDate, e.Title, e.Monitored, hasFile = e.MediaFileId is not null,
        };

        private static IEnumerable<object> Seasons(Series s) => s.Seasons.OrderBy(season => season.Number).Select(season =>
        {
            Episode[] episodes = [.. s.Episodes.Where(e => e.SeasonNumber == season.Number)];
            return new
            {
                season.Number, season.Monitored,
                episodes = episodes.Length,
                episodesWithFile = episodes.Count(e => e.MediaFileId is not null),
                missing = Missing(episodes),
                hasFile = Complete(episodes),
            };
        });

        private static int Missing(IEnumerable<Episode> episodes)
        {
            DateOnly today = DateOnly.FromDateTime(DateTime.Now);
            return episodes.Count(e => e.Monitored && e.MediaFileId is null && e.AirDate <= today);
        }

        private static bool Complete(IEnumerable<Episode> episodes) =>
            episodes.Where(e => e.Monitored).ToArray() is { Length: > 0 } monitored && monitored.All(e => e.MediaFileId is not null);
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
        hasMetadata = item.Torrent?.HasMetadata,
        seeds = item.Torrent?.Seeds,
        peers = item.Torrent?.Peers,
    };

    private static object ToNetworkDto(PublicIpCheck ip, TorrentEngine engine) => new
    {
        ip.PublicIp, ip.Org, ip.Country, ip.CheckedAt, ip.Error,
        engine.ListenPort, engine.OpenConnections,
    };
}
