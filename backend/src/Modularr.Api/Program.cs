using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Modularr.Api;
using Modularr.Core.Configuration;

// Every regex (release titles, Cardigann dates, definitions) runs on remote text: none may run for more than a moment.
// Set before the first Regex is created, the source-generated ones included.
AppContext.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(1));

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// The container sets MODULARR_CONFIG_DIR=/config (Dockerfile); dev keeps its config next to the API project.
string configDirectory = builder.Configuration["MODULARR_CONFIG_DIR"] ?? Path.Combine(builder.Environment.ContentRootPath, "config");
builder.Services.AddModularrCore(configDirectory);
builder.Services.AddModularrAuth(configDirectory);
builder.Services.AddArrCompat();
builder.Services.AddScoped<ApiEndpoints.ItemDtos>();
// Behind a reverse proxy the client's address, scheme and host come from X-Forwarded-*, trusted only from the proxies listed in
// TrustedProxies (addresses or CIDR ranges, ';'-separated), never by default: otherwise any client could forge them.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    foreach (string proxy in (builder.Configuration["TrustedProxies"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (proxy.Contains('/', StringComparison.Ordinal))
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(proxy));
        }
        else
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }
    }
});
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

WebApplication app = builder.Build();

// First, so the cookie's Secure flag, HSTS and every rate limit see the real client.
app.UseForwardedHeaders();
app.UseApiErrors();
// Only sent over HTTPS (Kestrel with a certificate, see README): browsers then refuse a downgrade to plain HTTP.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// The UI is never framed (clickjacking), MIME types are not sniffed, and remote poster hosts do not learn this server's address.
app.Use((context, next) =>
{
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    return next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();
// Explicit, after static files: the implicit UseRouting runs first, matches the SPA fallback and static files then skip.
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// The UI files stay public; the API needs a signed-in user unless an endpoint says otherwise, and an admin to change anything.
app.MapGroup("/api").RequireAuthorization().RequireAdminForWrites().MapModularrApi().MapCollectionApi().MapActivityApi().MapSubtitleApi().MapAuthApi().MapBackupApi().MapApiKeySettings();
// Sonarr/Radarr v3 for request apps: its own group, signed in by the API key only (see ArrCompatEndpoints).
app.MapArrCompat();
// linkarr's user provisioning, by the same API key (see ProvisionEndpoints).
app.MapProvisioning();

// Unknown /api routes must 404, not fall through to the SPA.
app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html");

app.Run();
