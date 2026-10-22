using System.Net;
using System.Net.Sockets;
using Modularr.Core.Downloads.Torrents;
using MonoTorrent;

namespace Modularr.Core.Tests.Downloads.Torrents;

// Magnet cases ported from the Sonarr and Radarr QBittorrentFixture (GPL-3.0); the rest exercises MonoTorrent over localhost.
public sealed class TorrentEngineTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly string _root = Directory.CreateTempSubdirectory("modularr-torrent-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("magnet:?xt=urn:btih:ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR&tr=udp", "CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951")]
    public async Task DownloadShouldGetHashFromMagnetUrl(string magnetUrl, string expectedHash)
    {
        await using TorrentEngine engine = await TorrentEngine.StartAsync(Options("magnet"));

        string id = await engine.AddMagnetAsync(magnetUrl, Path.Combine(_root, "downloads"), "tv");

        Assert.Equal(expectedHash, id);
    }

    [Fact]
    public async Task DownloadShouldRefuseMagnetIfNoTrackersProvidedAndDhtIsDisabled()
    {
        await using TorrentEngine engine = await TorrentEngine.StartAsync(Options("magnet"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.AddMagnetAsync("magnet:?xt=urn:btih:ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR", Path.Combine(_root, "downloads"), "tv"));
    }

    [Fact]
    public async Task DownloadShouldAcceptMagnetIfTrackersProvidedAndDhtIsDisabled()
    {
        await using TorrentEngine engine = await TorrentEngine.StartAsync(Options("magnet"));

        string id = await engine.AddMagnetAsync("magnet:?xt=urn:btih:ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR&tr=udp://abc", Path.Combine(_root, "downloads"), "tv");

        Assert.Equal("tv", engine.Get(id)?.Category);
    }

    [Fact]
    public async Task DownloadsFromSeederOverLocalhostThenRestoresAndRemoves()
    {
        string seedDirectory = Directory.CreateDirectory(Path.Combine(_root, "seed")).FullName;
        string downloadDirectory = Path.Combine(_root, "downloads");
        byte[] content = new byte[3 * 1024 * 1024 + 123];
        new Random(42).NextBytes(content);
        await File.WriteAllBytesAsync(Path.Combine(seedDirectory, "Series.S01E01.mkv"), content, TestContext.Current.CancellationToken);
        byte[] torrentFile = (await new TorrentCreator().CreateAsync(new TorrentFileSource(Path.Combine(seedDirectory, "Series.S01E01.mkv")), TestContext.Current.CancellationToken)).Encode();

        int seederPort = FreePort();
        await using TorrentEngine seeder = await TorrentEngine.StartAsync(Options("seeder") with { ListenPort = seederPort });
        string id = await seeder.AddTorrentFileAsync(torrentFile, seedDirectory, "tv");
        await WaitForAsync(seeder, id, s => s.Status == DownloadItemStatus.Completed);

        TorrentEngineOptions leecherOptions = Options("leecher") with { ListenPort = FreePort(), SeedRatioLimit = 0 };
        await using (TorrentEngine leecher = await TorrentEngine.StartAsync(leecherOptions))
        {
            Assert.Equal(id, await leecher.AddTorrentFileAsync(torrentFile, downloadDirectory, "tv"));
            Assert.True(await leecher.AddPeerAsync(id, new IPEndPoint(IPAddress.Loopback, seederPort)));

            TorrentStatus done = await WaitForAsync(leecher, id, s => s.Status == DownloadItemStatus.Completed);
            Assert.Equal(1.0, done.Progress);
            Assert.Equal(content.LongLength, done.TotalSize);
            Assert.Equal(Path.Combine(downloadDirectory, "Series.S01E01.mkv"), done.OutputPath);
            Assert.Equal(content, await File.ReadAllBytesAsync(done.OutputPath, TestContext.Current.CancellationToken));

            await leecher.EnforceSeedLimitsAsync();
            Assert.True(leecher.Get(id)?.CanBeRemoved);
        }

        await using (TorrentEngine restored = await TorrentEngine.StartAsync(leecherOptions))
        {
            TorrentStatus status = await WaitForAsync(restored, id, s => s.Status == DownloadItemStatus.Completed);
            Assert.Equal("tv", status.Category);
            Assert.Equal(1.0, status.Progress);
            Assert.True(status.CanBeRemoved);

            Assert.True(await restored.RemoveAsync(id, deleteData: true));
            Assert.Null(restored.Get(id));
            Assert.False(File.Exists(status.OutputPath));
        }
    }

    [Fact]
    public async Task PauseAndResumeAreRestored()
    {
        TorrentEngineOptions options = Options("pause");
        string id;
        await using (TorrentEngine engine = await TorrentEngine.StartAsync(options))
        {
            id = await engine.AddMagnetAsync("magnet:?xt=urn:btih:ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR&tr=udp://abc", Path.Combine(_root, "downloads"), "movies");
            Assert.True(await engine.PauseAsync(id));
            Assert.Equal(DownloadItemStatus.Paused, engine.Get(id)?.Status);
        }

        await using (TorrentEngine engine = await TorrentEngine.StartAsync(options))
        {
            Assert.Equal(DownloadItemStatus.Paused, engine.Get(id)?.Status);
            Assert.Equal("movies", engine.Get(id)?.Category);
            Assert.True(await engine.ResumeAsync(id));
            Assert.NotEqual(DownloadItemStatus.Paused, engine.Get(id)?.Status);
            Assert.True(await engine.RemoveAsync(id, deleteData: false));
            Assert.Empty(engine.List());
        }
    }

    private TorrentEngineOptions Options(string name) => new()
    {
        StateDirectory = Path.Combine(_root, "state-" + name),
        ListenPort = FreePort(),
        DhtEnabled = false,
        LocalPeerDiscoveryEnabled = false,
    };

    private static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<TorrentStatus> WaitForAsync(TorrentEngine engine, string id, Func<TorrentStatus, bool> condition)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        TorrentStatus? status = null;
        while (!timeout.IsCancellationRequested)
        {
            status = engine.Get(id);
            if (status is not null && condition(status))
            {
                return status;
            }

            await Task.Delay(100, CancellationToken.None);
        }

        throw new TimeoutException($"Torrent {id} never reached the expected state, last seen: {status}");
    }
}
