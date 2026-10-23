using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modularr.Api;
using Modularr.Core.Accounts;
using Modularr.Core.Configuration;
using Modularr.Core.Persistence;

namespace Modularr.Core.Tests.Accounts;

// The /api/provision endpoints over HTTP, on a loopback Kestrel with the API's auth wiring and none of the background services.
public sealed class ProvisionEndpointsTests : IAsyncLifetime
{
    private const string ApiKey = "provision-test-key";

    private readonly string _configDirectory = Directory.CreateTempSubdirectory("modularr-provision-").FullName;
    private WebApplication? _app;
    private HttpClient _client = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(_configDirectory, ModularrOptions.FileName), $$"""{ "Auth": { "ApiKey": "{{ApiKey}}" } }""");
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        ServiceDescriptor[] hosting = [.. builder.Services.Where(d => d.ServiceType == typeof(IHostedService))];
        builder.Services.AddModularrCore(_configDirectory);
        foreach (ServiceDescriptor background in builder.Services.Where(d => d.ServiceType == typeof(IHostedService) && !hosting.Contains(d)).ToList())
        {
            builder.Services.Remove(background);
        }

        builder.Services.AddModularrAuth(_configDirectory);
        builder.Services.AddArrCompat();
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        _app = builder.Build();
        _app.UseApiErrors();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();
        _app.MapGroup("/api").RequireAuthorization().RequireAdminForWrites().MapAuthApi();
        _app.MapProvisioning();
        using (IServiceScope scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ModularrDbContext>().Database.MigrateAsync(Ct);
        }

        await _app.StartAsync(Ct);
        _client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        Directory.Delete(_configDirectory, recursive: true);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, string? key = ApiKey)
    {
        HttpRequestMessage request = new(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.Add("X-Api-Key", key);
        }

        return _client.SendAsync(request, Ct);
    }

    private async Task<User> AddAdminAsync()
    {
        using IServiceScope scope = _app!.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AccountService>().CreateAsync("owner", "password1", UserRole.Admin, Ct);
    }

    [Fact]
    public async Task EveryEndpointNeedsTheApiKeyAndNeverTakesTheCookie()
    {
        await AddAdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/auth/login", new { username = "owner", password = "password1" }, Ct)).StatusCode);

        (HttpMethod Method, string Path, object? Body)[] endpoints =
        [
            (HttpMethod.Get, "/api/provision/users", null),
            (HttpMethod.Put, "/api/provision/users/L1", new { username = "alice", role = "User", disabled = false }),
            (HttpMethod.Delete, "/api/provision/users/L1", null),
            (HttpMethod.Put, "/api/provision/managed", new { name = "linkarr", url = "https://hub.example.com" }),
            (HttpMethod.Delete, "/api/provision/managed", null),
            (HttpMethod.Get, "/api/provision/stats", null),
        ];
        foreach ((HttpMethod method, string path, object? body) in endpoints)
        {
            // Signed in as an admin by cookie, without a key.
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(method, path, body, key: null)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/provision/users", key: "wrong")).StatusCode);
    }

    // Before the first IP check there is no address; nothing downloads; every media folder reports its volume's space.
    [Fact]
    public async Task StatsReportIpDownloadsStorageAndVersion()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/api/provision/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonObject stats = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!;
        Assert.Null(stats["publicIp"]);
        Assert.Equal(0, (int)stats["downloads"]!["active"]!);
        Assert.Equal(0, (long)stats["downloads"]!["downloadBps"]!);
        JsonArray storage = stats["storage"]!.AsArray();
        Assert.NotEmpty(storage);
        Assert.All(storage, s => Assert.True((long)s!["totalBytes"]! >= (long)s["freeBytes"]! && (long)s["totalBytes"]! > 0));
        Assert.False(string.IsNullOrEmpty((string?)stats["version"]));
    }

    [Fact]
    public async Task UsersAreUpsertedListedAndDeleted()
    {
        User owner = await AddAdminAsync();

        HttpResponseMessage created = await SendAsync(HttpMethod.Put, "/api/provision/users/L1", new { username = "alice", role = "User", password = "password1", disabled = false });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        int id = (int)(await created.Content.ReadFromJsonAsync<JsonObject>(Ct))!["id"]!;
        HttpResponseMessage updated = await SendAsync(HttpMethod.Put, "/api/provision/users/L1", new { username = "alice2", role = "Admin", disabled = false });
        Assert.Equal(id, (int)(await updated.Content.ReadFromJsonAsync<JsonObject>(Ct))!["id"]!);

        JsonArray users = (await (await SendAsync(HttpMethod.Get, "/api/provision/users")).Content.ReadFromJsonAsync<JsonArray>(Ct))!;
        Assert.Equal(
            [$"{id}|alice2|Admin|L1", $"{owner.Id}|owner|Admin|"],
            users.Select(u => $"{u!["id"]}|{u["username"]}|{u["role"]}|{u["externalId"]}"));

        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Put, "/api/provision/users/L2", new { username = "alice2", role = "User", disabled = false })).StatusCode);
        // A never-linked local account with the name is adopted only on linkarr's explicit adopt, not duplicated.
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Put, "/api/provision/users/L3", new { username = "owner", role = "Admin", disabled = false })).StatusCode);
        HttpResponseMessage adopted = await SendAsync(HttpMethod.Put, "/api/provision/users/L3", new { username = "owner", role = "Admin", disabled = false, adopt = true });
        Assert.Equal(owner.Id, (int)(await adopted.Content.ReadFromJsonAsync<JsonObject>(Ct))!["id"]!);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Put, "/api/provision/users/L2", new { username = "bob", disabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, "/api/provision/users/L1")).StatusCode);
        Assert.Single((await (await SendAsync(HttpMethod.Get, "/api/provision/users")).Content.ReadFromJsonAsync<JsonArray>(Ct))!);
    }

    [Fact]
    public async Task DisablingSignsTheUserOutAndBlocksSignIn()
    {
        await AddAdminAsync();
        await SendAsync(HttpMethod.Put, "/api/provision/users/L1", new { username = "alice", role = "User", password = "password1", disabled = false });
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/auth/login", new { username = "alice", password = "password1" }, Ct)).StatusCode);
        Assert.Equal("alice", (string?)(await _client.GetFromJsonAsync<JsonObject>("/api/auth/state", Ct))!["user"]?["username"]);

        await SendAsync(HttpMethod.Put, "/api/provision/users/L1", new { username = "alice", role = "User", disabled = true });

        Assert.Null((await _client.GetFromJsonAsync<JsonObject>("/api/auth/state", Ct))!["user"]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/auth/login", new { username = "alice", password = "password1" }, Ct)).StatusCode);
    }

    // No first-run setup: with no account, nothing but linkarr's provisioning creates one.
    [Fact]
    public async Task WithNoAccountsOnlyProvisioningCreatesTheFirstAdmin()
    {
        JsonObject state = (await _client.GetFromJsonAsync<JsonObject>("/api/auth/state", Ct))!;
        Assert.False(state.ContainsKey("setupRequired"));
        HttpStatusCode setup = (await _client.PostAsJsonAsync("/api/auth/setup", new { username = "intruder", password = "password1" }, Ct)).StatusCode;
        Assert.True(setup is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"setup: {setup}");

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, "/api/provision/users/A1", new { username = "admin", role = "Admin", password = "password1", disabled = false })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "password1" }, Ct)).StatusCode);
        Assert.Equal("Admin", (string?)(await _client.GetFromJsonAsync<JsonObject>("/api/auth/state", Ct))!["user"]?["role"]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/auth/login", new { username = "intruder", password = "password1" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task TheLastAdminCannotBeDeleted()
    {
        await SendAsync(HttpMethod.Put, "/api/provision/users/A1", new { username = "admin", role = "Admin", password = "password1", disabled = false });

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Delete, "/api/provision/users/A1")).StatusCode);
    }

    [Fact]
    public async Task TheLastEnabledAdminCannotBeDisabled()
    {
        await SendAsync(HttpMethod.Put, "/api/provision/users/A1", new { username = "admin", role = "Admin", password = "password1", disabled = false });
        await SendAsync(HttpMethod.Put, "/api/provision/users/A2", new { username = "admin2", role = "Admin", password = "password1", disabled = true });

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Put, "/api/provision/users/A1", new { username = "admin", role = "Admin", disabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "password1" }, Ct)).StatusCode);

        // With another enabled admin it goes through.
        await SendAsync(HttpMethod.Put, "/api/provision/users/A2", new { username = "admin2", role = "Admin", disabled = false });
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, "/api/provision/users/A1", new { username = "admin", role = "Admin", disabled = true })).StatusCode);
    }

    [Fact]
    public async Task TheManagingHubIsSavedShownAndCleared()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Put, "/api/provision/managed", new { name = "linkarr", url = "ftp://hub" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Put, "/api/provision/managed", new { name = "linkarr", url = "https://hub.example.com" })).StatusCode);

        JsonObject state = (await _client.GetFromJsonAsync<JsonObject>("/api/auth/state", Ct))!;
        Assert.Equal(("linkarr", "https://hub.example.com"), ((string?)state["managed"]?["name"], (string?)state["managed"]?["url"]));
        Assert.Contains("\"Managed\"", await File.ReadAllTextAsync(Path.Combine(_configDirectory, ModularrOptions.FileName), Ct), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, "/api/provision/managed")).StatusCode);
        Assert.Null((await _client.GetFromJsonAsync<JsonObject>("/api/auth/state", Ct))!["managed"]);
    }

    [Fact]
    public async Task TheHookTokenIsSavedButNeverShown()
    {
        const string Token = "s3cret-hook-token";
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Put, "/api/provision/managed", new { name = "linkarr", url = "https://hub.example.com", hookToken = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Put, "/api/provision/managed", new { name = "linkarr", url = "https://hub.example.com/", hookToken = Token })).StatusCode);

        Assert.DoesNotContain(Token, await _client.GetStringAsync("/api/auth/state", Ct), StringComparison.Ordinal);
        Assert.Contains(Token, await File.ReadAllTextAsync(Path.Combine(_configDirectory, ModularrOptions.FileName), Ct), StringComparison.Ordinal);
        ManagedOptions? managed = _app!.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ModularrOptions>>().CurrentValue.Managed;
        Assert.Equal(("https://hub.example.com/api/hooks/modularr", Token), (managed?.Hook()?.Url, managed?.Hook()?.Token));

        // Sent again without one, the hook is off.
        await SendAsync(HttpMethod.Put, "/api/provision/managed", new { name = "linkarr", url = "https://hub.example.com" });
        Assert.DoesNotContain(Token, await File.ReadAllTextAsync(Path.Combine(_configDirectory, ModularrOptions.FileName), Ct), StringComparison.Ordinal);
    }

    // Accounts are managed in linkarr: the user admin and own-account routes are gone, even for a signed-in admin.
    [Fact]
    public async Task AccountManagementRoutesAreGone()
    {
        User owner = await AddAdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/auth/login", new { username = "owner", password = "password1" }, Ct)).StatusCode);

        (HttpMethod Method, string Path, object? Body)[] removed =
        [
            (HttpMethod.Post, "/api/auth/password", new { currentPassword = "password1", newPassword = "password2" }),
            (HttpMethod.Patch, "/api/auth/profile", new { username = "renamed" }),
            (HttpMethod.Get, "/api/users", null),
            (HttpMethod.Post, "/api/users", new { username = "bob", password = "password1", role = "User" }),
            (HttpMethod.Patch, $"/api/users/{owner.Id}", new { role = "User" }),
            (HttpMethod.Delete, $"/api/users/{owner.Id}", null),
        ];
        foreach ((HttpMethod method, string path, object? body) in removed)
        {
            HttpStatusCode status = (await SendAsync(method, path, body, key: null)).StatusCode;
            Assert.True(status is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{method} {path}: {status}");
        }

        // Nothing changed: the name and password still sign in.
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/auth/login", new { username = "owner", password = "password1" }, Ct)).StatusCode);
    }
}
