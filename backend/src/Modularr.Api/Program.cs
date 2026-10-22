using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// The container sets MODULARR_CONFIG_DIR=/config (Dockerfile); dev keeps its config next to the API project.
string configDirectory = builder.Configuration["MODULARR_CONFIG_DIR"] ?? Path.Combine(builder.Environment.ContentRootPath, "config");
builder.Services.AddModularrCore(configDirectory);

WebApplication app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

RouteGroupBuilder api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new { status = "ok" }));
api.MapGet("/config", (IOptions<ModularrOptions> options) => Results.Ok(options.Value));

// Unknown /api routes must 404, not fall through to the SPA.
app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html");

app.Run();
