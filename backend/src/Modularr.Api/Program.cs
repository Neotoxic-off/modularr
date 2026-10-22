using System.Text.Json.Serialization;
using Modularr.Api;
using Modularr.Core.Configuration;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// The container sets MODULARR_CONFIG_DIR=/config (Dockerfile); dev keeps its config next to the API project.
string configDirectory = builder.Configuration["MODULARR_CONFIG_DIR"] ?? Path.Combine(builder.Environment.ContentRootPath, "config");
builder.Services.AddModularrCore(configDirectory);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

WebApplication app = builder.Build();

app.UseApiErrors();
app.UseDefaultFiles();
app.UseStaticFiles();

// shortcut: no auth on /api (grab only fetches links on catalog indexer sites, but anyone reaching the port can add to the library and
// start downloads), add an API key with the front's login before exposing the port beyond a trusted LAN or storing any credential.
app.MapGroup("/api").MapModularrApi();

// Unknown /api routes must 404, not fall through to the SPA.
app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html");

app.Run();
