using Microsoft.Extensions.Options;
using Modularr.Core.Accounts;
using Modularr.Core.Configuration;
using Modularr.Core.Downloads;
using Modularr.Core.Library;
using Modularr.Core.Persistence;

namespace Modularr.Api;

// linkarr's user provisioning. Like /api/v3: its own group, signed in by the API key only (never the cookie, so outside the cookie
// group's same-origin filter), failed keys throttled by ApiKeyAuthenticationHandler.
public static class ProvisionEndpoints
{
    // Role is required: omitting it must not silently create an admin.
    // Adopt: linkarr's admin explicitly adopts the never-linked local account of that name; never sent by plain provisioning.
    public sealed record ProvisionUserRequest(string Username, UserRole? Role, string? Password, bool Disabled, bool Adopt = false);

    // HookToken: when set, modularr posts its grab, import and failure events to {Url}/api/hooks/modularr with the
    // token in the X-Linkarr-Token header.
    // HookUrl: where modularr reaches linkarr for that hook (an address inside Docker); Url when absent.
    public sealed record ManagedRequest(string? Name, string? Url, string? HookToken, string? HookUrl = null);

    public static IEndpointRouteBuilder MapProvisioning(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder provision = app.MapGroup("/api/provision").RequireAuthorization(ArrCompatEndpoints.ApiKeyPolicy);

        provision.MapGet("/users", async (AccountService accounts, CancellationToken ct) => (await accounts.ListAsync(ct)).Select(u => new
        {
            u.Id,
            u.Username,
            u.Role,
            externalId = u.ExternalProvider == AccountService.LinkarrProvider ? u.ExternalId : null,
        }));
        provision.MapPut("/users/{externalId}", async (string externalId, ProvisionUserRequest request, AccountService accounts, CancellationToken ct) =>
        {
            User user = await accounts.ProvisionAsync(externalId, request.Username,
                request.Role ?? throw new ArgumentException("Role is required (Admin or User).", nameof(request)), request.Password, request.Disabled, request.Adopt, ct);
            return new { user.Id };
        });
        provision.MapDelete("/users/{externalId}", async (string externalId, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.DeprovisionAsync(externalId, ct);
            return Results.NoContent();
        });

        provision.MapPut("/managed", async (ManagedRequest request, SettingsFile settings, CancellationToken ct) =>
        {
            await settings.SaveManagedAsync(new ManagedOptions { Name = request.Name?.Trim(), Url = request.Url?.Trim(), HookToken = request.HookToken, HookUrl = request.HookUrl?.Trim() }, ct);
            return Results.NoContent();
        });
        provision.MapDelete("/managed", async (SettingsFile settings, CancellationToken ct) =>
        {
            await settings.SaveManagedAsync(null, ct);
            return Results.NoContent();
        });

        // linkarr's overview: the address downloads leave from (the VPN's when there is one), live transfer rates and the media folders' space.
        provision.MapGet("/stats", async (PublicIpMonitor ip, DownloadService downloads, IOptionsMonitor<ModularrOptions> options, CancellationToken ct) =>
        {
            IReadOnlyList<DownloadItem> active = await downloads.ListAsync(inProgressOnly: true, ct);
            PublicIpCheck check = ip.Last;
            PathOptions paths = options.CurrentValue.Paths;
            return new
            {
                publicIp = check.PublicIp is null ? null : new { ip = check.PublicIp, country = check.Country, isp = check.Org, checkedAt = check.CheckedAt },
                downloads = new
                {
                    active = active.Count,
                    downloadBps = active.Sum(d => d.Torrent?.DownloadRate ?? 0),
                    uploadBps = active.Sum(d => d.Torrent?.UploadRate ?? 0),
                },
                storage = new[] { paths.Movies, paths.Series, paths.Anime, paths.Downloads }.Distinct(StringComparer.Ordinal)
                    .Select(path => (path, volume: DashboardService.VolumeOf(path)))
                    .Where(f => f.volume is not null)
                    .Select(f => new { f.path, freeBytes = f.volume!.Value.Free, totalBytes = f.volume.Value.Total }),
                version = typeof(ProvisionEndpoints).Assembly.GetName().Version?.ToString(3),
            };
        });
        return app;
    }
}
