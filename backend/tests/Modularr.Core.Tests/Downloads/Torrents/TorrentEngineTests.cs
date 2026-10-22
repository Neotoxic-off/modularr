using System.Net;
using System.Net.Sockets;
using Modularr.Core.Downloads.Torrents;
using MonoTorrent;
using MonoTorrent.Client;

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
    public async Task StartsFreshWhenTheStateFileIsTruncated()
    {
        TorrentEngineOptions options = Options("corrupt");
        await using (TorrentEngine engine = await TorrentEngine.StartAsync(options))
        {
            await engine.AddMagnetAsync("magnet:?xt=urn:btih:ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR&tr=udp://abc", Path.Combine(_root, "downloads"), "tv");
        }

        string statePath = Path.Combine(options.StateDirectory, "engine.state");
        byte[] state = await File.ReadAllBytesAsync(statePath, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(statePath, state[..(state.Length / 2)], TestContext.Current.CancellationToken);

        await using TorrentEngine restarted = await TorrentEngine.StartAsync(options);

        Assert.Empty(restarted.List());
        Assert.True(File.Exists(statePath + ".corrupt"));
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

    [Fact]
    public async Task PausingTwiceThenRemovingDoesNotThrow()
    {
        await using TorrentEngine engine = await TorrentEngine.StartAsync(Options("pause-twice"));
        string id = await engine.AddMagnetAsync("magnet:?xt=urn:btih:ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR&tr=udp://abc", Path.Combine(_root, "downloads"), "movies");

        Assert.True(await engine.PauseAsync(id));
        Assert.True(await engine.PauseAsync(id));
        Assert.True(await engine.RemoveAsync(id, deleteData: true));
        Assert.Empty(engine.List());
    }

    [Fact]
    public async Task RejectedTorrentFileIsNeverStarted()
    {
        (_, byte[] torrentFile) = await CreateTorrentAsync("Movie.2020.1080p.WEB-DL", ("Movie.2020.1080p.WEB-DL.mkv", 1024 * 1024), ("Codec.exe", 1024));
        string downloads = Path.Combine(_root, "downloads");
        TorrentEngineOptions options = Options("reject");
        string id;

        await using (TorrentEngine engine = await TorrentEngine.StartAsync(options))
        {
            id = await engine.AddTorrentFileAsync(torrentFile, downloads, "movies");

            TorrentStatus status = engine.Get(id)!;
            Assert.Equal(DownloadItemStatus.Failed, status.Status);
            Assert.Equal("Caution: Found executable file with extension: .exe", status.Message);
        }

        await using (TorrentEngine restored = await TorrentEngine.StartAsync(options))
        {
            Assert.Equal(DownloadItemStatus.Failed, restored.Get(id)?.Status);
        }

        Assert.False(Directory.Exists(downloads) && Directory.EnumerateFiles(downloads, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task MagnetSkipsTheSampleAndExtrasOnceItsMetadataArrives()
    {
        (string seedDirectory, byte[] torrentFile) = await CreateTorrentAsync(
            "Movie.2020.1080p.WEB-DL",
            ("Movie.2020.1080p.WEB-DL.mkv", 3 * 1024 * 1024),
            ("Sample/movie.2020.1080p.sample.mkv", 2 * 1024 * 1024),
            ("RARBG.txt", 2 * 1024 * 1024));
        await using SeederEngine seeder = await SeederEngine.StartAsync(torrentFile, seedDirectory, Path.Combine(_root, "seeder"));

        await using TorrentEngine engine = await TorrentEngine.StartAsync(Options("magnet-ok"));
        string id = await engine.AddMagnetAsync($"magnet:?xt=urn:btih:{seeder.Id}&tr=udp://abc", Path.Combine(_root, "downloads"), "movies");
        Assert.True(await engine.AddPeerAsync(id, seeder.EndPoint));

        TorrentStatus done = await WaitForAsync(engine, id, s => s.Status == DownloadItemStatus.Completed);

        Assert.Equal(1.0, done.Progress);
        Assert.Equal(
            [("Movie.2020.1080p.WEB-DL.mkv", true), ("RARBG.txt", false), ("Sample/movie.2020.1080p.sample.mkv", false)],
            done.Files.Select(f => (Path: f.Path.Replace('\\', '/'), Done: f.Progress == 1.0)).OrderBy(f => f.Path, StringComparer.Ordinal));
    }

    // Pausing cancels the first metadata wait, the resume starts a new one that still checks the content.
    [Fact]
    public async Task MagnetPausedBeforeItsMetadataIsCheckedAfterResume()
    {
        (string seedDirectory, byte[] torrentFile) = await CreateTorrentAsync(
            "Movie.2020.1080p.WEB-DL", ("Movie.2020.1080p.WEB-DL.mkv", 3 * 1024 * 1024), ("RARBG.txt", 2 * 1024 * 1024));
        await using SeederEngine seeder = await SeederEngine.StartAsync(torrentFile, seedDirectory, Path.Combine(_root, "seeder"));

        await using TorrentEngine engine = await TorrentEngine.StartAsync(Options("magnet-resume"));
        string id = await engine.AddMagnetAsync($"magnet:?xt=urn:btih:{seeder.Id}&tr=udp://abc", Path.Combine(_root, "downloads"), "movies");
        Assert.True(await engine.PauseAsync(id));
        Assert.True(await engine.ResumeAsync(id));
        Assert.True(await engine.AddPeerAsync(id, seeder.EndPoint));

        TorrentStatus done = await WaitForAsync(engine, id, s => s.Status == DownloadItemStatus.Completed);

        Assert.Equal(
            [("Movie.2020.1080p.WEB-DL.mkv", true), ("RARBG.txt", false)],
            done.Files.Select(f => (Path: f.Path.Replace('\\', '/'), Done: f.Progress == 1.0)).OrderBy(f => f.Path, StringComparer.Ordinal));
    }

    [Fact]
    public async Task MagnetWithMalwareDownloadsNoPayload()
    {
        (string seedDirectory, byte[] torrentFile) = await CreateTorrentAsync(
            "Movie.2020.1080p.WEB-DL", ("Movie.2020.1080p.WEB-DL.mkv", 3 * 1024 * 1024), ("Movie.2020.1080p.WEB-DL.lnk", 1024));
        await using SeederEngine seeder = await SeederEngine.StartAsync(torrentFile, seedDirectory, Path.Combine(_root, "seeder"));

        await using TorrentEngine engine = await TorrentEngine.StartAsync(Options("magnet-bad"));
        string id = await engine.AddMagnetAsync($"magnet:?xt=urn:btih:{seeder.Id}&tr=udp://abc", Path.Combine(_root, "downloads"), "movies");
        Assert.True(await engine.AddPeerAsync(id, seeder.EndPoint));

        TorrentStatus rejected = await WaitForAsync(engine, id, s => s.Status == DownloadItemStatus.Failed);

        Assert.Contains(".lnk", rejected.Message, StringComparison.Ordinal);
        Assert.Equal(0, rejected.DownloadedBytes);
        Assert.All(rejected.Files, f => Assert.Equal(0, f.Progress));
    }

    // Real swarm over DHT with legal content (Blender's Sintel: video, subtitles, poster); the torrent and its data are removed.
    [Fact]
    public async Task LiveMagnetPassesTheContentCheck()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("MODULARR_LIVE_TORRENTS") == "1", "Set MODULARR_LIVE_TORRENTS=1 to join a live swarm.");
        await using TorrentEngine engine = await TorrentEngine.StartAsync(Options("live") with { DhtEnabled = true });
        string id = await engine.AddMagnetAsync("magnet:?xt=urn:btih:08ada5a7a6183aae1e09d831df6748d566095a10&dn=Sintel&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Fexplodie.org%3A6969", Path.Combine(_root, "downloads"), "movies");

        TorrentStatus status = await WaitForAsync(engine, id, s => s.HasMetadata, TimeSpan.FromMinutes(5));

        Assert.NotEqual(DownloadItemStatus.Failed, status.Status);
        Assert.Contains(status.Files, f => f.Path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
        Assert.True(await engine.RemoveAsync(id, deleteData: true));
    }

    // A folder torrent of random files, with the directory that seeds it.
    private async Task<(string SeedDirectory, byte[] TorrentFile)> CreateTorrentAsync(string name, params (string Path, int Size)[] files)
    {
        string seedDirectory = Directory.CreateDirectory(Path.Combine(_root, "seed-" + Guid.NewGuid().ToString("N"))).FullName;
        string folder = Path.Combine(seedDirectory, name);
        foreach ((string path, int size) in files)
        {
            string file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            byte[] content = new byte[size];
            new Random(size).NextBytes(content);
            await File.WriteAllBytesAsync(file, content, TestContext.Current.CancellationToken);
        }

        return (seedDirectory, (await new TorrentCreator(TorrentType.V1Only).CreateAsync(new TorrentFileSource(folder), TestContext.Current.CancellationToken)).Encode());
    }

    // A bare MonoTorrent engine: the modularr engine would refuse to seed the malware torrents.
    private sealed class SeederEngine(ClientEngine engine, string id, IPEndPoint endPoint) : IAsyncDisposable
    {
        public string Id => id;

        public IPEndPoint EndPoint => endPoint;

        public static async Task<SeederEngine> StartAsync(byte[] torrentFile, string seedDirectory, string cacheDirectory)
        {
            int port = FreePort();
            ClientEngine engine = new(new EngineSettingsBuilder
            {
                CacheDirectory = cacheDirectory,
                ListenEndPoints = new Dictionary<string, IPEndPoint> { ["ipv4"] = new(IPAddress.Loopback, port) },
                DhtEndPoint = null,
                AllowLocalPeerDiscovery = false,
                AllowPortForwarding = false,
            }.ToSettings());
            TorrentManager manager = await engine.AddAsync(Torrent.Load(torrentFile), seedDirectory);
            await manager.StartAsync();
            while (manager.State != TorrentState.Seeding)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            return new SeederEngine(engine, manager.InfoHashes.V1OrV2.ToHex(), new IPEndPoint(IPAddress.Loopback, port));
        }

        public async ValueTask DisposeAsync()
        {
            await engine.StopAllAsync();
            engine.Dispose();
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

    private static async Task<TorrentStatus> WaitForAsync(TorrentEngine engine, string id, Func<TorrentStatus, bool> condition, TimeSpan? within = null)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(within ?? Timeout);
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
