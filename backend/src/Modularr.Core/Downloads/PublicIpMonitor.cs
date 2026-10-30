using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modularr.Core.Configuration;

namespace Modularr.Core.Downloads;

public sealed record PublicIpCheck(string? PublicIp, string? Org, string? Country, DateTimeOffset? CheckedAt, string? Error);

// The address the app's traffic leaves from, as a public what-is-my-IP service sees it: behind a VPN it is the VPN's exit,
// which is what the torrent engine (same container, same route) shows its peers. Checked every 5 minutes and on demand.
// shortcut: assumes the engine shares the app's route (true in the container), bind-to-interface setups would need their own check.
public sealed partial class PublicIpMonitor(IHttpClientFactory httpClientFactory, ILogger<PublicIpMonitor> logger) : BackgroundService
{
    public static readonly Uri InfoUrl = new("https://ipinfo.io/json");
    public static readonly Uri FallbackUrl = new("https://api.ipify.org");
    public const int MaxResponseBytes = 16 * 1024;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _checking = new(1, 1);

    public PublicIpCheck Last { get; private set; } = new(null, null, null, null, null);

    // ipinfo.io answers {"ip": "...", "org": "AS39351 31173 Services AB", "country": "SE", ...}; the AS number is dropped.
    public static PublicIpCheck ParseInfo(string json, DateTimeOffset now)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string? ip = Text(root, "ip");
        if (ip is null || !IPAddress.TryParse(ip, out _))
        {
            throw new FormatException("The IP service did not answer an IP address.");
        }

        string? org = Text(root, "org") is string raw ? AsNumber().Replace(raw, string.Empty) : null;
        return new PublicIpCheck(ip, string.IsNullOrWhiteSpace(org) ? null : org, Text(root, "country"), now, null);

        static string? Text(JsonElement root, string name) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim() is { Length: > 0 and <= 200 } text ? text : null
                : null;
    }

    // One check at a time; a refresh right after another returns the fresh result instead of asking again.
    public async Task<PublicIpCheck> RefreshAsync(CancellationToken cancellationToken)
    {
        await _checking.WaitAsync(cancellationToken);
        try
        {
            if (Last.CheckedAt is DateTimeOffset at && DateTimeOffset.UtcNow - at < MinRefreshInterval)
            {
                return Last;
            }

            Last = await CheckAsync(cancellationToken);
            return Last;
        }
        finally
        {
            _checking.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);
        do
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.JobFailed("Public IP check", ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<PublicIpCheck> CheckAsync(CancellationToken cancellationToken)
    {
        using HttpClient http = httpClientFactory.CreateClient(nameof(PublicIpMonitor));
        try
        {
            return ParseInfo(await http.GetStringAsync(InfoUrl, cancellationToken), DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException && !cancellationToken.IsCancellationRequested)
        {
            // ipinfo.io rate-limited or down: the bare address from ipify still answers the main question.
            try
            {
                string ip = (await http.GetStringAsync(FallbackUrl, cancellationToken)).Trim();
                return IPAddress.TryParse(ip, out _)
                    ? new PublicIpCheck(ip, null, null, DateTimeOffset.UtcNow, null)
                    : Failed("The IP service did not answer an IP address.");
            }
            catch (Exception fallback) when (fallback is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return Failed(ex.Message);
            }
        }
    }

    private static PublicIpCheck Failed(string error) => new(null, null, null, DateTimeOffset.UtcNow, error);

    [GeneratedRegex(@"^AS\d+\s+")]
    private static partial Regex AsNumber();
}
