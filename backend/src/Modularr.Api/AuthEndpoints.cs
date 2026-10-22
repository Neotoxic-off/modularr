using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Modularr.Core.Accounts;
using Modularr.Core.Persistence;

namespace Modularr.Api;

// Cookie sign-in against the local accounts; every /api route needs a signed-in user unless marked anonymous.
public static class AuthEndpoints
{
    public const string AdminPolicy = "Admin";

    private const string LoginRateLimit = "login";
    private const string StampClaim = "modularr:stamp";

    public sealed record Credentials(string Username, string Password);

    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    public sealed record CreateUserRequest(string Username, string Password, UserRole Role);

    public sealed record UpdateUserRequest(UserRole? Role, string? Password);

    public static IServiceCollection AddModularrAuth(this IServiceCollection services, string configDirectory)
    {
        // Persisted keys keep the cookies valid across container restarts.
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(configDirectory, "keys")));

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
                context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }

    public static RouteGroupBuilder MapAuthApi(this RouteGroupBuilder api)
    {
        api.MapGet("/auth/state", State).AllowAnonymous();
        api.MapPost("/auth/setup", Setup).AllowAnonymous().RequireRateLimiting(LoginRateLimit);
        api.MapPost("/auth/login", Login).AllowAnonymous().RequireRateLimiting(LoginRateLimit);
        api.MapPost("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync();
            return Results.NoContent();
        });
        api.MapPost("/auth/password", async (ChangePasswordRequest request, HttpContext context, AccountService accounts, CancellationToken ct) =>
        {
            User user = await accounts.ChangePasswordAsync(UserId(context.User), request.CurrentPassword, request.NewPassword, ct);
            await SignInAsync(context, user); // the stamp changed: keep this session, end the others
            return ToDto(user);
        });

        RouteGroupBuilder users = api.MapGroup("/users").RequireAuthorization(AdminPolicy);
        users.MapGet("/", async (AccountService accounts, CancellationToken ct) => (await accounts.ListAsync(ct)).Select(ToDto));
        users.MapPost("/", async (CreateUserRequest request, AccountService accounts, CancellationToken ct) =>
            ToDto(await accounts.CreateAsync(request.Username, request.Password, request.Role, ct)));
        users.MapPatch("/{id:int}", async (int id, UpdateUserRequest request, AccountService accounts, CancellationToken ct) =>
            ToDto(await accounts.UpdateAsync(id, request.Role, request.Password, ct)));
        users.MapDelete("/{id:int}", async (int id, HttpContext context, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.DeleteAsync(id, UserId(context.User), ct);
            return Results.NoContent();
        });
        return api;
    }

    private static async Task<object> State(HttpContext context, AccountService accounts, CancellationToken ct)
    {
        User? user = context.User.Identity?.IsAuthenticated == true ? await accounts.FindAsync(UserId(context.User), ct) : null;
        return new { setupRequired = await accounts.IsSetupRequiredAsync(ct), user = user is null ? null : ToDto(user) };
    }

    private static async Task<IResult> Setup(Credentials request, HttpContext context, AccountService accounts, CancellationToken ct)
    {
        if (await accounts.SetupAsync(request.Username, request.Password, ct) is not User user)
        {
            return Results.Conflict(new { error = "Setup is already done." });
        }

        await SignInAsync(context, user);
        return Results.Ok(ToDto(user));
    }

    private static async Task<IResult> Login(Credentials request, HttpContext context, AccountService accounts, CancellationToken ct)
    {
        if (await accounts.AuthenticateAsync(request.Username, request.Password, ct) is not User user)
        {
            return Results.Json(new { error = "Invalid username or password." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        await SignInAsync(context, user);
        return Results.Ok(ToDto(user));
    }

    private static Task SignInAsync(HttpContext context, User user) => context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(StampClaim, Stamp(user)),
        ],
        CookieAuthenticationDefaults.AuthenticationScheme)));

    // A cookie dies with its account, a password change or a role change, without waiting for its 30 days.
    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        AccountService accounts = context.HttpContext.RequestServices.GetRequiredService<AccountService>();
        User? user = context.Principal is ClaimsPrincipal principal ? await accounts.FindAsync(UserId(principal), context.HttpContext.RequestAborted) : null;
        if (user is null || context.Principal?.FindFirstValue(StampClaim) != Stamp(user))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
        }
    }

    private static string Stamp(User user) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{user.Username}|{user.Role}|{user.PasswordHash}")))[..32];

    private static int UserId(ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), System.Globalization.CultureInfo.InvariantCulture, out int id) ? id : 0;

    private static Task WriteError(HttpResponse response, int status, string message)
    {
        response.StatusCode = status;
        return response.WriteAsJsonAsync(new { error = message });
    }

    private static object ToDto(User user) => new { user.Id, user.Username, user.Role, user.Created };
}
