using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Downloads;
using Modularr.Core.Library;
using Modularr.Core.Metadata;
using Modularr.Core.Persistence;
using Modularr.Core.Search;

namespace Modularr.Api;

// The part of Sonarr's and Radarr's v3 API that request apps (Overseerr, Jellyseerr, Ombi) use, so modularr can stand in for both.
// Signed in by the API key only (never the cookie), as an admin: these clients add titles and start searches.
public static class ArrCompatEndpoints
{
    public const string ApiKeyPolicy = "ApiKey";

    public sealed record AddOptions(bool SearchForMissingEpisodes = false, bool SearchForMovie = false);

    // The fields read from the full Sonarr series / Radarr movie the clients send back; the others are ignored.
    public sealed record SeriesBody(int Id = 0, int TvdbId = 0, int? QualityProfileId = null, bool Monitored = true, IReadOnlyList<ArrSeason>? Seasons = null, AddOptions? AddOptions = null);

    public sealed record MovieBody(int Id = 0, int TmdbId = 0, int? QualityProfileId = null, bool Monitored = true, AddOptions? AddOptions = null);

    public sealed record CommandBody(string? Name, int? SeriesId = null, int? SeasonNumber = null, IReadOnlyList<int>? MovieIds = null, IReadOnlyList<int>? EpisodeIds = null);

    public static IServiceCollection AddArrCompat(this IServiceCollection services)
    {
        // Not the default scheme: the rest of /api never reads the key, and the policy below never reads the cookie.
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
        services.AddAuthorizationBuilder().AddPolicy(ApiKeyPolicy, policy => policy
            .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
            .RequireRole(nameof(UserRole.Admin)));
        return services;
    }

    // Under the cookie-authenticated /api group: admins only, the key gives full control of the library to whoever holds it.
    public static RouteGroupBuilder MapApiKeySettings(this RouteGroupBuilder api)
    {
        api.MapGet("/settings/apikey", (IOptionsMonitor<ModularrOptions> options) => ApiKeyAnswer.Of(options.CurrentValue.Auth))
            .RequireAuthorization(AuthEndpoints.AdminPolicy);
        api.MapPost("/settings/apikey", async (SettingsFile settings, IOptionsMonitor<ModularrOptions> options, CancellationToken ct) =>
        {
            if (options.CurrentValue.Auth.ApiKeyFromEnvironment)
            {
                return Results.Conflict(new { error = $"The API key is set by {AuthOptions.ApiKeyVariable}: change it there." });
            }

            await settings.SaveApiKeyAsync(AuthOptions.GenerateApiKey(), ct);
            return Results.Ok(new { apiKey = options.CurrentValue.Auth.ApiKey });
        }).RequireAuthorization(AuthEndpoints.AdminPolicy);
        return api;
    }

    public static IEndpointRouteBuilder MapArrCompat(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder v3 = app.MapGroup("/api/v3").RequireAuthorization(ApiKeyPolicy);

        // A Sonarr v3 version: the clients then use language profiles, which v4 dropped.
        v3.MapGet("/system/status", () => new { appName = "Modularr", version = "3.0.10.1567", urlBase = "", isProduction = true });
        v3.MapGet("/qualityprofile", async (QualityProfileService profiles, CancellationToken ct) => (await profiles.ListAsync(ct)).Select(p => new { p.Id, p.Name }));
        v3.MapGet("/languageprofile", () => new[] { new { id = ArrCompat.LanguageProfileId, name = "Any" } });
        v3.MapGet("/rootfolder", (IOptionsMonitor<ModularrOptions> options) => ArrCompat.RootFolders(options.CurrentValue.Paths));
        v3.MapGet("/tag", () => Array.Empty<object>());
        v3.MapGet("/queue", QueueAsync);

        v3.MapGet("/series", async (int? tvdbId, LibraryService library, CancellationToken ct) =>
            (await library.ListSeriesAsync(ct)).Where(s => tvdbId is null || s.TvdbId == tvdbId).Select(s => ArrCompat.ToSonarr(s, Today())));
        v3.MapGet("/series/{id:int}", async (int id, LibraryService library, CancellationToken ct) => ArrCompat.ToSonarr(await library.GetSeriesAsync(id, ct), Today()));
        v3.MapGet("/series/lookup", async (string? term, ModularrDbContext db, SeriesMetadataClient metadata, IOptionsMonitor<ModularrOptions> options, CancellationToken ct) =>
            await LookupSeriesAsync(term, db, metadata, options.CurrentValue, ct))
            .RequireRateLimiting(AuthEndpoints.OutboundRateLimit);
        v3.MapPost("/series", AddSeriesAsync);
        v3.MapPut("/series", (SeriesBody body, LibraryService library, CancellationToken ct) => UpdateSeriesAsync(body.Id, body, library, ct));
        v3.MapPut("/series/{id:int}", (int id, SeriesBody body, LibraryService library, CancellationToken ct) => UpdateSeriesAsync(id, body, library, ct));

        v3.MapGet("/movie", async (int? tmdbId, LibraryService library, CancellationToken ct) =>
            (await library.ListMoviesAsync(ct)).Where(m => tmdbId is null || m.Movie.TmdbId == tmdbId).Select(m => ArrCompat.ToRadarr(m.Movie, m.HasFile)));
        v3.MapGet("/movie/{id:int}", async (int id, LibraryService library, CancellationToken ct) =>
        {
            (Movie movie, MediaFile? file) = await library.GetMovieAsync(id, ct);
            return ArrCompat.ToRadarr(movie, file is not null);
        });
        v3.MapGet("/movie/lookup", async (string? term, ModularrDbContext db, MovieMetadataClient metadata, IOptionsMonitor<ModularrOptions> options, CancellationToken ct) =>
            await LookupMoviesAsync(term, db, metadata, options.CurrentValue, ct))
            .RequireRateLimiting(AuthEndpoints.OutboundRateLimit);
        // Radarr answers this one with a single movie, not a list.
        v3.MapGet("/movie/lookup/tmdb", async (int tmdbId, ModularrDbContext db, MovieMetadataClient metadata, IOptionsMonitor<ModularrOptions> options, CancellationToken ct) =>
            await FindMovieAsync(tmdbId, db, metadata, options.CurrentValue, ct) is ArrMovie movie ? Results.Ok(movie) : Results.NotFound(new { error = $"No movie with TMDB id {tmdbId}." }))
            .RequireRateLimiting(AuthEndpoints.OutboundRateLimit);
        v3.MapPost("/movie", AddMovieAsync);
        v3.MapPut("/movie", (MovieBody body, LibraryService library, SearchRunner searches, CancellationToken ct) => UpdateMovieAsync(body.Id, body, library, searches, ct));
        v3.MapPut("/movie/{id:int}", (int id, MovieBody body, LibraryService library, SearchRunner searches, CancellationToken ct) => UpdateMovieAsync(id, body, library, searches, ct));

        v3.MapPost("/command", CommandAsync);
        v3.Map("/{**path}", () => Results.NotFound(new { error = "Not supported by modularr." }));
        return app;
    }

    private static async Task<ArrQueuePage> QueueAsync(
        DownloadService downloads, ModularrDbContext db, CancellationToken ct, int page = 1, int pageSize = 10, bool includeMovie = false, bool includeSeries = false)
    {
        IReadOnlyList<DownloadItem> items = await downloads.ListAsync(inProgressOnly: true, ct);
        int[] movieIds = includeMovie ? [.. items.Select(i => i.Download.MovieId).OfType<int>().Distinct()] : [];
        int[] seriesIds = includeSeries ? [.. items.Select(i => i.Download.SeriesId).OfType<int>().Distinct()] : [];
        Dictionary<int, Movie> movies = await db.Movies.AsNoTracking().Where(m => movieIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        Dictionary<int, Series> series = await db.Series.AsNoTracking().Where(s => seriesIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        return ArrCompat.Queue(items, page, pageSize, DateTime.UtcNow, movies, series);
    }

    // The library entry when there is one (with its id), else the metadata with id 0, as Sonarr's lookup answers.
    // Like Sonarr and Radarr: "tvdb:N" / "tmdb:N" finds one title, any other term is a text search (library titles keep their id).
    private const int LookupLimit = 20;

    private static async Task<IReadOnlyList<ArrSeries>> LookupSeriesAsync(string? term, ModularrDbContext db, SeriesMetadataClient metadata, ModularrOptions options, CancellationToken ct)
    {
        if (IdIn(term, "tvdb:") is int tvdbId)
        {
            return await FindSeriesAsync(tvdbId, db, metadata, options, ct) is ArrSeries found ? [found] : [];
        }

        if (string.IsNullOrWhiteSpace(term))
        {
            return [];
        }

        IReadOnlyList<SeriesInfo> hits = await metadata.SearchSeriesAsync(term.Trim(), cancellationToken: ct);
        int[] ids = [.. hits.Take(LookupLimit).Select(h => h.TvdbId)];
        Dictionary<int, Series> owned = await db.Series.AsNoTracking().Include(s => s.Episodes).Where(s => ids.Contains(s.TvdbId)).ToDictionaryAsync(s => s.TvdbId, ct);
        return [.. hits.Take(LookupLimit).Select(info => owned.TryGetValue(info.TvdbId, out Series? series)
            ? ArrCompat.ToSonarr(series, Today())
            : ArrCompat.ToSonarr(LibraryService.ToSeries(info, options.Media.For(LibraryService.TypeOf(info) == SeriesType.Anime ? MediaType.Anime : MediaType.Series).ProfileId, options.Paths, DateTime.UtcNow), Today()))];
    }

    private static async Task<IReadOnlyList<ArrMovie>> LookupMoviesAsync(string? term, ModularrDbContext db, MovieMetadataClient metadata, ModularrOptions options, CancellationToken ct)
    {
        if (IdIn(term, "tmdb:") is int tmdbId)
        {
            return await FindMovieAsync(tmdbId, db, metadata, options, ct) is ArrMovie found ? [found] : [];
        }

        if (string.IsNullOrWhiteSpace(term))
        {
            return [];
        }

        IReadOnlyList<MovieInfo> hits = await metadata.SearchMoviesAsync(term.Trim(), ct);
        int[] ids = [.. hits.Take(LookupLimit).Select(h => h.TmdbId)];
        Dictionary<int, Movie> owned = await db.Movies.AsNoTracking().Where(m => ids.Contains(m.TmdbId)).ToDictionaryAsync(m => m.TmdbId, ct);
        int[] ownedIds = [.. owned.Values.Select(m => m.Id)];
        HashSet<int> withFile = [.. await db.MediaFiles.Where(f => f.MovieId != null && ownedIds.Contains(f.MovieId.Value)).Select(f => f.MovieId!.Value).ToListAsync(ct)];
        return [.. hits.Take(LookupLimit).Select(info => owned.TryGetValue(info.TmdbId, out Movie? movie)
            ? ArrCompat.ToRadarr(movie, withFile.Contains(movie.Id))
            : ArrCompat.ToRadarr(LibraryService.ToMovie(info, options.Media.Movies.ProfileId, options.Paths), hasFile: false))];
    }

    private static async Task<ArrSeries?> FindSeriesAsync(int tvdbId, ModularrDbContext db, SeriesMetadataClient metadata, ModularrOptions options, CancellationToken ct)
    {
        if (await db.Series.AsNoTracking().Include(s => s.Episodes).FirstOrDefaultAsync(s => s.TvdbId == tvdbId, ct) is Series series)
        {
            return ArrCompat.ToSonarr(series, Today());
        }

        try
        {
            SeriesInfo info = await metadata.GetSeriesAsync(tvdbId, cancellationToken: ct);
            MediaType category = LibraryService.TypeOf(info) == SeriesType.Anime ? MediaType.Anime : MediaType.Series;
            return ArrCompat.ToSonarr(LibraryService.ToSeries(info, options.Media.For(category).ProfileId, options.Paths, DateTime.UtcNow), Today());
        }
        catch (MetadataNotFoundException)
        {
            return null;
        }
    }

    private static async Task<ArrMovie?> FindMovieAsync(int tmdbId, ModularrDbContext db, MovieMetadataClient metadata, ModularrOptions options, CancellationToken ct)
    {
        if (await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.TmdbId == tmdbId, ct) is Movie movie)
        {
            return ArrCompat.ToRadarr(movie, await db.MediaFiles.AnyAsync(f => f.MovieId == movie.Id, ct));
        }

        try
        {
            return ArrCompat.ToRadarr(LibraryService.ToMovie(await metadata.GetMovieAsync(tmdbId, ct), options.Media.Movies.ProfileId, options.Paths), hasFile: false);
        }
        catch (MetadataNotFoundException)
        {
            return null;
        }
    }

    // The clients send every season with its flag. Anime or not follows the metadata, like the UI's add, not the root folder.
    private static async Task<IResult> AddSeriesAsync(SeriesBody body, LibraryService library, CancellationToken ct)
    {
        SeriesMonitor monitor = !body.Monitored ? SeriesMonitor.None : body.Seasons is null ? SeriesMonitor.All : SeriesMonitor.Custom;
        int[]? seasons = body.Seasons?.Where(s => s.Monitored).Select(s => s.SeasonNumber).ToArray();
        Series series = await library.AddSeriesAsync(body.TvdbId, body.QualityProfileId, version: null, monitor, seasons, body.AddOptions?.SearchForMissingEpisodes ?? false, ct);
        return Results.Created($"/api/v3/series/{series.Id}", ArrCompat.ToSonarr(series, Today()));
    }

    // A series requested again: its monitored flag, profile and seasons.
    private static async Task<ArrSeries> UpdateSeriesAsync(int id, SeriesBody body, LibraryService library, CancellationToken ct)
    {
        Series series = await library.UpdateSeriesAsync(id, new ItemUpdate { Monitored = body.Monitored, QualityProfileId = body.QualityProfileId }, ct);
        foreach (ArrSeason season in body.Seasons ?? [])
        {
            if (series.Seasons.FirstOrDefault(s => s.Number == season.SeasonNumber) is Season current && current.Monitored != season.Monitored)
            {
                series = await library.SetSeasonMonitoredAsync(id, season.SeasonNumber, season.Monitored, ct);
            }
        }

        return ArrCompat.ToSonarr(series, Today());
    }

    private static async Task<IResult> AddMovieAsync(MovieBody body, LibraryService library, CancellationToken ct)
    {
        Movie movie = await library.AddMovieAsync(body.TmdbId, body.QualityProfileId, version: null, body.Monitored && (body.AddOptions?.SearchForMovie ?? false), ct);
        if (!body.Monitored)
        {
            await library.UpdateMovieAsync(movie.Id, new ItemUpdate { Monitored = false }, ct);
        }

        (Movie added, MediaFile? file) = await library.GetMovieAsync(movie.Id, ct);
        return Results.Created($"/api/v3/movie/{added.Id}", ArrCompat.ToRadarr(added, file is not null));
    }

    // Overseerr monitors an unmonitored movie again this way, with addOptions.searchForMovie for the search.
    private static async Task<ArrMovie> UpdateMovieAsync(int id, MovieBody body, LibraryService library, SearchRunner searches, CancellationToken ct)
    {
        await library.UpdateMovieAsync(id, new ItemUpdate { Monitored = body.Monitored, QualityProfileId = body.QualityProfileId }, ct);
        (Movie movie, MediaFile? file) = await library.GetMovieAsync(id, ct);
        if (body.AddOptions?.SearchForMovie == true && movie.Monitored && file is null)
        {
            searches.Enqueue(new SearchJob(SeriesId: null, id));
        }

        return ArrCompat.ToRadarr(movie, file is not null);
    }

    // The searches the clients start; each runs in the background like the UI's search buttons.
    private static async Task<IResult> CommandAsync(CommandBody body, ModularrDbContext db, SearchRunner searches, CancellationToken ct)
    {
        IEnumerable<SearchJob> jobs = body.Name?.ToUpperInvariant() switch
        {
            "SERIESSEARCH" when body.SeriesId is int seriesId => [new SearchJob(seriesId, MovieId: null)],
            "SEASONSEARCH" when body.SeriesId is int seriesId && body.SeasonNumber is int season => [new SearchJob(seriesId, MovieId: null, season)],
            "MOVIESSEARCH" when body.MovieIds is { } movieIds => movieIds.Select(movieId => new SearchJob(SeriesId: null, movieId)),
            // Searches run by season: each episode's season is searched.
            "EPISODESEARCH" when body.EpisodeIds is { } episodeIds => (await db.Episodes.AsNoTracking().Where(e => episodeIds.Contains(e.Id))
                .Select(e => new { e.SeriesId, e.SeasonNumber }).Distinct().ToListAsync(ct)).Select(e => new SearchJob(e.SeriesId, MovieId: null, e.SeasonNumber)),
            _ => throw new ArgumentException($"Unsupported command '{body.Name}'."),
        };
        foreach (SearchJob job in jobs)
        {
            searches.Enqueue(job);
        }

        return Results.Created("/api/v3/command", new { id = 0, name = body.Name, status = "queued" });
    }

    private static int? IdIn(string? term, string prefix) =>
        term is not null && term.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(term.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? id : null;

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);
}

// GET /api/settings/apikey. A key from MODULARR_API_KEY is the deployment's (linkarr provisions with it): it never comes back.
public sealed record ApiKeyAnswer(string? ApiKey, bool FromEnvironment)
{
    public static ApiKeyAnswer Of(AuthOptions auth)
    {
        ArgumentNullException.ThrowIfNull(auth);
        return auth.ApiKeyFromEnvironment ? new ApiKeyAnswer(null, true) : new ApiKeyAnswer(auth.ApiKey, false);
    }
}

// X-Api-Key header or apikey query (some clients only send that) against Auth.ApiKey; never cookies, so no CSRF surface.
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IOptionsMonitor<ModularrOptions> settings)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    private const string ThrottledItem = "modularr:apikey-throttled";

    // Failed keys per address, 10 a minute like sign-in, so the key cannot be guessed online. Only failures count.
    private static readonly PartitionedRateLimiter<string> Failures = PartitionedRateLimiter.Create<string, string>(address =>
        RateLimitPartition.GetFixedWindowLimiter(address, _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? presented = Request.Headers["X-Api-Key"].FirstOrDefault() ?? Request.Query["apikey"].FirstOrDefault();
        if (string.IsNullOrEmpty(presented))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // The right key always passes: behind a proxy every client shares one address, and a 256-bit key cannot be guessed anyway,
        // so throttling only slows wrong keys instead of locking the real client out.
        if (!AuthOptions.ApiKeyMatches(settings.CurrentValue.Auth.ApiKey, presented))
        {
            string address = Context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
            using RateLimitLease lease = Failures.AttemptAcquire(address);
            if (!lease.IsAcquired)
            {
                Context.Items[ThrottledItem] = true;
                return Task.FromResult(AuthenticateResult.Fail("Too many failed API key attempts."));
            }

            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        ClaimsPrincipal principal = new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "apikey"), new Claim(ClaimTypes.Name, "API key"), new Claim(ClaimTypes.Role, nameof(UserRole.Admin))], SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        bool throttled = Context.Items.ContainsKey(ThrottledItem);
        Response.StatusCode = throttled ? StatusCodes.Status429TooManyRequests : StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new { error = throttled ? "Too many failed API key attempts, try again in a minute." : "Missing or invalid API key." });
    }
}
