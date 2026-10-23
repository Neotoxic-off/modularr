using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Modularr.Core.Accounts;
using Modularr.Core.Configuration;
using Modularr.Core.Persistence;

namespace Modularr.Api;

// Cookie sign-in against the local accounts; every /api route needs a signed-in user unless marked anonymous.
public static class AuthEndpoints
{
    public const string AdminPolicy = "Admin";

    private const string LoginRateLimit = "login";
    public const string OutboundRateLimit = "outbound";

    // The only writes a member may make: their own sessions and picture, and the unread marker.
    private static readonly HashSet<string> MemberWrites = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login", "/api/auth/logout", "/api/auth/avatar",
        "/api/auth/sessions/end-others", "/api/activity/read",
    };
    private const string StampClaim = "modularr:stamp";
    private const string SessionClaim = "modularr:session";

    public sealed record Credentials(string Username, string Password);

    public static IServiceCollection AddModularrAuth(this IServiceCollection services, string configDirectory)
    {
        // Persisted keys keep the cookies valid across container restarts.
        // The keys sign every session cookie: their folder is readable by this account only.
        string keys = Path.Combine(configDirectory, "keys");
        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Directory.CreateDirectory(keys, OwnerOnly);
            File.SetUnixFileMode(keys, OwnerOnly);
        }

        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keys));

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "modularr";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context => WriteError(context.Response, StatusCodes.Status401Unauthorized, "Not signed in.");
            options.Events.OnRedirectToAccessDenied = context => WriteError(context.Response, StatusCodes.Status403Forbidden, "Admin only.");
            options.Events.OnValidatePrincipal = ValidatePrincipalAsync;
        });
        services.AddAuthorizationBuilder().AddPolicy(AdminPolicy, policy => policy.RequireRole(nameof(UserRole.Admin)));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(context.Connection.RemoteIpAddress),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
            options.AddPolicy(OutboundRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }

    public static RouteGroupBuilder MapAuthApi(this RouteGroupBuilder api)
    {
        api.MapGet("/auth/state", State).AllowAnonymous();
        api.MapPost("/auth/login", Login).AllowAnonymous().RequireRateLimiting(LoginRateLimit);
        // The session row goes with the logout, so a copy of this cookie stops working too.
        api.MapPost("/auth/logout", async (HttpContext context, AccountService accounts, CancellationToken ct) =>
        {
            if (context.User.FindFirstValue(SessionClaim) is string session)
            {
                await accounts.EndSessionAsync(session, ct);
            }

            await context.SignOutAsync();
            return Results.NoContent();
        });
        // The signed-in user's own picture and other devices; the account itself (name, password) is managed in linkarr.
        api.MapPut("/auth/avatar", async (HttpContext context, AccountService accounts, CancellationToken ct) =>
        {
            if (context.Request.ContentLength > AccountService.MaxAvatarBytes)
            {
                return Results.BadRequest(new { error = $"The picture must be at most {AccountService.MaxAvatarBytes / 1024} KB." });
            }

            // Read at most one byte past the limit, whatever the declared length, so a chunked upload cannot grow memory.
            using MemoryStream image = new();
            byte[] buffer = new byte[16 * 1024];
            int read;
            while ((read = await context.Request.Body.ReadAsync(buffer, ct)) > 0 && image.Length <= AccountService.MaxAvatarBytes)
            {
                image.Write(buffer, 0, read);
            }

            return Results.Ok(ToDto(await accounts.SetAvatarAsync(UserId(context.User), image.ToArray(), ct)));
        });
        api.MapDelete("/auth/avatar", async (HttpContext context, AccountService accounts, CancellationToken ct) =>
            ToDto(await accounts.RemoveAvatarAsync(UserId(context.User), ct)));
        api.MapPost("/auth/sessions/end-others", async (HttpContext context, AccountService accounts, CancellationToken ct) =>
        {
            if (context.User.FindFirstValue(SessionClaim) is string session)
            {
                await accounts.EndOtherSessionsAsync(UserId(context.User), session, ct);
            }

            return Results.NoContent();
        });
        // Any signed-in user sees the pictures (the header). The URL is versioned, so it can be cached for long.
        api.MapGet("/avatars/{id:int}", async (int id, HttpContext context, AccountService accounts, CancellationToken ct) =>
        {
            if (await accounts.FindAvatarAsync(id, ct) is not UserAvatar avatar)
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            return Results.File(avatar.Content, avatar.ContentType);
        });

        return api;
    }

    // Members only read: every write needs an admin, except signing in and out, their own picture and the unread marker.
    public static RouteGroupBuilder RequireAdminForWrites(this RouteGroupBuilder api)
    {
        api.AddEndpointFilter(async (context, next) =>
        {
            HttpRequest request = context.HttpContext.Request;
            bool safe = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);
            if (!safe && !IsSameOrigin(request))
            {
                return Results.Json(new { error = "Cross-origin request refused." }, statusCode: StatusCodes.Status403Forbidden);
            }

            // Exact paths, not prefixes: a route added later under /api/auth must not become member-writable by accident.
            bool memberMay = safe || MemberWrites.Contains(request.Path.Value?.TrimEnd('/') ?? "");
            return memberMay || context.HttpContext.User.IsInRole(nameof(UserRole.Admin))
                ? await next(context)
                : Results.Json(new { error = "Admin only." }, statusCode: StatusCodes.Status403Forbidden);
        });
        return api;
    }

    // CSRF: SameSite=Strict still lets another origin of the same site (another port, a sibling subdomain) send the cookie, and
    // bodyless POSTs need no preflight. A browser's write must come from this origin; a client sending neither header is not a browser.
    private static bool IsSameOrigin(HttpRequest request)
    {
        string? fetchSite = request.Headers["Sec-Fetch-Site"];
        if (!string.IsNullOrEmpty(fetchSite))
        {
            return fetchSite is "same-origin" or "none";
        }

        string? origin = request.Headers.Origin;
        return string.IsNullOrEmpty(origin)
            || (Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase));
    }

    // managed: the linkarr hub that owns the accounts here, when one is set (its hook token is never serialized).
    private static async Task<object> State(HttpContext context, AccountService accounts, IOptionsMonitor<ModularrOptions> options, CancellationToken ct)
    {
        User? user = context.User.Identity?.IsAuthenticated == true ? await accounts.FindAsync(UserId(context.User), ct) : null;
        return new { user = user is null ? null : ToDto(user), managed = options.CurrentValue.Managed };
    }

    private static async Task<IResult> Login(Credentials request, HttpContext context, AccountService accounts, CancellationToken ct)
    {
        User? user = await accounts.AuthenticateAsync(request.Username, request.Password, ct);
        if (user is null)
        {
            return Results.Json(new { error = "Invalid username or password." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        await SignInAsync(context, user, await accounts.StartSessionAsync(user.Id, ct));
        return Results.Ok(ToDto(user));
    }

    private static Task SignInAsync(HttpContext context, User user, string session) => context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(SessionClaim, session),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(StampClaim, Stamp(user)),
        ],
        CookieAuthenticationDefaults.AuthenticationScheme)));

    // A cookie dies with its account, a password or role change (from linkarr) or its own logout, without waiting for its 30 days.
    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        AccountService accounts = context.HttpContext.RequestServices.GetRequiredService<AccountService>();
        User? user = context.Principal is ClaimsPrincipal principal ? await accounts.FindAsync(UserId(principal), context.HttpContext.RequestAborted) : null;
        string? session = context.Principal?.FindFirstValue(SessionClaim);
        if (user is null || user.Disabled || context.Principal?.FindFirstValue(StampClaim) != Stamp(user) || session is null
            || !await accounts.IsSessionOpenAsync(session, user.Id, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
        }
    }

    private static string Stamp(User user) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{user.Username}|{user.Role}|{user.PasswordHash}")))[..32];

    // One bucket per IPv4 address but per IPv6 /64: a single host owns a whole /64 and would otherwise get unlimited buckets.
    internal static string ClientKey(System.Net.IPAddress? address)
    {
        if (address is null)
        {
            return string.Empty;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        byte[] bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new System.Net.IPAddress(bytes) + "/64";
    }

    internal static int UserId(ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), System.Globalization.CultureInfo.InvariantCulture, out int id) ? id : 0;

    private static Task WriteError(HttpResponse response, int status, string message)
    {
        response.StatusCode = status;
        return response.WriteAsJsonAsync(new { error = message });
    }

    private static object ToDto(User user) => new
    {
        user.Id,
        user.Username,
        user.Role,
        user.Created,
        external = user.ExternalProvider,
        avatar = user.AvatarUpdated is DateTime updated ? $"/api/avatars/{user.Id}?v={updated.Ticks}" : null,
    };
}
