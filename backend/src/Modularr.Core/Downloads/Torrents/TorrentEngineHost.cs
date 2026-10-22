using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;

namespace Modularr.Core.Downloads.Torrents;

// Starts the one TorrentEngine with the app and stops it (saving state) on shutdown; inject this to reach the engine.
public sealed class TorrentEngineHost(IOptions<ModularrOptions> options) : IHostedService
{
    private TorrentEngine? _engine;

    public TorrentEngine Engine => _engine ?? throw new InvalidOperationException("The torrent engine is not started.");

    public async Task StartAsync(CancellationToken cancellationToken) =>
        _engine = await TorrentEngine.StartAsync(options.Value.Torrents, options.Value.PostProcessing);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_engine is not null)
        {
            await _engine.DisposeAsync();
            _engine = null;
        }
    }
}
