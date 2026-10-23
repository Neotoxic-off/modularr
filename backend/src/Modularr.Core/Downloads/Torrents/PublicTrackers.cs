using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;

namespace Modularr.Core.Downloads.Torrents;

// Adds the community list of public trackers (ngosang/trackerslist, "best" set) to every public torrent so swarms are found
// faster. Refreshed daily and cached in the torrent state folder, so a failed download keeps the last good list.
public sealed class PublicTrackers(
    IHttpClientFactory httpClientFactory, TorrentEngineHost engineHost, IOptions<ModularrOptions> options, ILogger<PublicTrackers> logger) : BackgroundService
{
    public static readonly Uri ListUrl = new("https://raw.githubusercontent.com/ngosang/trackerslist/master/trackers_best.txt");
    public const int MaxTrackers = 100;
    public const int MaxListBytes = 256 * 1024;
    private const string CacheFile = "public-trackers.txt";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

    private string CachePath => Path.Combine(options.Value.Torrents.StateDirectory, CacheFile);

    // One announce URL per line; only udp/http/https to a public host is kept, so the list can never aim the engine at the local network.
    public static IReadOnlyList<Uri> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return
        [
            .. text.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length is > 0 and <= 512)
                .Select(line => Uri.TryCreate(line, UriKind.Absolute, out Uri? uri) ? uri : null)
                .OfType<Uri>()
                .Where(uri => uri.Scheme is "udp" or "http" or "https" && uri.UserInfo.Length == 0 && PublicAddress.IsPublicHost(uri))
                .DistinctBy(uri => uri.AbsoluteUri)
                .Take(MaxTrackers),
        ];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (File.Exists(CachePath))
            {
                await ApplyAsync(Parse(await File.ReadAllTextAsync(CachePath, stoppingToken)));
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.JobFailed("Public tracker list", ex);
            // An unreadable cache only means waiting for the download below.
        }

        using PeriodicTimer timer = new(RefreshInterval);
        do
        {
            try
            {
                string text = await DownloadAsync(stoppingToken);
                IReadOnlyList<Uri> trackers = Parse(text);
                if (trackers.Count > 0)
                {
                    string temp = CachePath + ".tmp";
                    await File.WriteAllTextAsync(temp, string.Join('\n', trackers.Select(t => t.AbsoluteUri)), stoppingToken);
                    File.Move(temp, CachePath, overwrite: true);
                    await ApplyAsync(trackers);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.JobFailed("Public tracker list", ex);
                // Offline or GitHub down: keep the cached list and try again tomorrow.
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<string> DownloadAsync(CancellationToken cancellationToken)
    {
        using HttpClient http = httpClientFactory.CreateClient(nameof(PublicTrackers));
        using HttpResponseMessage response = await http.GetAsync(ListUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private Task ApplyAsync(IReadOnlyList<Uri> trackers) => engineHost.Engine.SetPublicTrackersAsync(trackers);
}
