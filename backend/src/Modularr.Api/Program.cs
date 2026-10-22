using System.Text.Json.Serialization;
using Modularr.Api;
using Modularr.Core.Configuration;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// The container sets MODULARR_CONFIG_DIR=/config (Dockerfile); dev keeps its config next to the API project.
string configDirectory = builder.Configuration["MODULARR_CONFIG_DIR"] ?? Path.Combine(builder.Environment.ContentRootPath, "config");
builder.Services.AddModularrCore(configDirectory);
builder.Services.AddModularrAuth(configDirectory);
builder.Services.AddScoped<ApiEndpoints.ItemDtos>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

WebApplication app = builder.Build();

app.UseApiErrors();
app.UseDefaultFiles();
app.UseStaticFiles();
// Explicit, after static files: the implicit UseRouting runs first, matches the SPA fallback and static files then skip.
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// The UI files stay public; the API needs a signed-in user unless an endpoint says otherwise.
app.MapGroup("/api").RequireAuthorization().MapModularrApi().MapAuthApi();

// Unknown /api routes must 404, not fall through to the SPA.
app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html");

app.Run();
