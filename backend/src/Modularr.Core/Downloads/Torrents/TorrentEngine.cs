using System.Net;
using System.Text.Json;
using Modularr.Core.PostProcessing;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.PiecePicking;

namespace Modularr.Core.Downloads.Torrents;

// In-process BitTorrent client owning one MonoTorrent ClientEngine; replaces the external qBittorrent of Sonarr/Radarr.
public sealed class TorrentEngine : IAsyncDisposable
{
    private const string EngineStateFile = "engine.state";
    private const string TagsFile = "torrents.json";

    private static readonly TimeSpan SeedLimitCheckInterval = TimeSpan.FromSeconds(30);

    private readonly ClientEngine _engine;
    private readonly TorrentEngineOptions _options;
    private readonly PostProcessingOptions _content;
    private readonly Dictionary<string, TorrentTag> _tags;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();

    // One metadata wait per magnet: pause, remove and every new start cancel the previous one.
    private readonly Dictionary<TorrentManager, CancellationTokenSource> _metadataWaits = [];
    private readonly Task _seedLimitLoop;

    private TorrentEngine(ClientEngine engine, TorrentEngineOptions options, PostProcessingOptions content, Dictionary<string, TorrentTag> tags)
    {
        _engine = engine;
        _options = options;
        _content = content;
        _tags = tags;
        _seedLimitLoop = RunSeedLimitLoopAsync(_stopping.Token);
    }

    private string EngineStatePath => Path.Combine(_options.StateDirectory, EngineStateFile);

    private string TagsPath => Path.Combine(_options.StateDirectory, TagsFile);

    // Last failure of the background seed-limit check, null once a check succeeds again.
    public Exception? SeedLimitError { get; private set; }

    // The content check judges each torrent's file list with the post-processing extension lists.
    public static async Task<TorrentEngine> StartAsync(TorrentEngineOptions options, PostProcessingOptions? content = null)
    {
        Directory.CreateDirectory(options.StateDirectory);
        string statePath = Path.Combine(options.StateDirectory, EngineStateFile);
        string tagsPath = Path.Combine(options.StateDirectory, TagsFile);
        EngineSettings settings = BuildSettings(options);

        ClientEngine engine = await RestoreAsync(statePath, settings) ?? new ClientEngine(settings);

        Dictionary<string, TorrentTag> tags = File.Exists(tagsPath)
            ? JsonSerializer.Deserialize<Dictionary<string, TorrentTag>>(await File.ReadAllBytesAsync(tagsPath)) ?? []
            : [];

        TorrentEngine started = new(engine, options, content ?? new PostProcessingOptions(), tags);
        foreach (TorrentManager manager in engine.Torrents)
        {
            if (tags.GetValueOrDefault(ToId(manager.InfoHashes))?.Stopped != true)
            {
                await started.StartCheckedAsync(manager);
            }
            else if (FastResume.TryLoad(engine.Settings.GetFastResumePath(manager.InfoHashes), out FastResume? fastResume))
            {
                // MonoTorrent only reads fast-resume on start; without this a stopped torrent would report 0% until resumed.
                // MonoTorrent 3.0.2 saves hybrid v1/v2 fast-resume under the v1 hash only, so rebind it to the full hashes.
                await manager.LoadFastResumeAsync(new FastResume(manager.InfoHashes, fastResume.Bitfield, fastResume.UnhashedPieces));
            }
        }

        return started;
    }

    public async Task<string> AddMagnetAsync(string magnetUri, string saveDirectory, string category)
    {
        MagnetLink magnet = MagnetLink.Parse(magnetUri);

        // Same refusal as Sonarr/Radarr: without trackers only DHT can find peers.
        if (magnet.AnnounceUrls is not { Count: > 0 } && !_options.DhtEnabled)
        {
            throw new ArgumentException("Magnet link has no trackers and DHT is disabled.", nameof(magnetUri));
        }

        return await AddAsync(magnet.InfoHashes, () => _engine.AddAsync(magnet, saveDirectory), category);
    }

    public async Task<string> AddTorrentFileAsync(ReadOnlyMemory<byte> torrentFile, string saveDirectory, string category)
    {
        Torrent torrent = Torrent.Load(torrentFile.Span);
        return await AddAsync(torrent.InfoHashes, () => _engine.AddAsync(torrent, saveDirectory), category);
    }

    public IReadOnlyList<TorrentStatus> List() => [.. _engine.Torrents.Select(ToStatus)];

    public TorrentStatus? Get(string id) => Find(id) is TorrentManager manager ? ToStatus(manager) : null;

    public async Task<bool> PauseAsync(string id)
    {
        if (Find(id) is not TorrentManager manager)
        {
            return false;
        }

        CancelMetadataWait(manager);
        await manager.StopAsync();
        UpdateTag(id, tag => tag with { Stopped = true });
        await SaveAsync();
        return true;
    }

    public async Task<bool> ResumeAsync(string id)
    {
        if (Find(id) is not TorrentManager manager)
        {
            return false;
        }

        await StartCheckedAsync(manager);
        UpdateTag(id, tag => tag with { Stopped = tag.Rejection is not null });
        await SaveAsync();
        return true;
    }

    public async Task<bool> RemoveAsync(string id, bool deleteData)
    {
        if (Find(id) is not TorrentManager manager)
        {
            return false;
        }

        CancelMetadataWait(manager);
        if (manager.State != TorrentState.Stopped)
        {
            await manager.StopAsync();
        }

        await _engine.RemoveAsync(manager, deleteData ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.CacheDataOnly);
        lock (_tags)
        {
            _tags.Remove(ToId(manager.InfoHashes));
        }

        await SaveAsync();
        return true;
    }

    // Asks the trackers, DHT and local peer discovery for peers again: the stall check's retry.
    public async Task<bool> ReannounceAsync(string id)
    {
        if (Find(id) is not TorrentManager manager)
        {
            return false;
        }

        await manager.TrackerManager.AnnounceAsync(CancellationToken.None);
        if (manager.CanUseDht)
        {
            await manager.DhtAnnounceAsync();
        }

        if (manager.CanUseLocalPeerDiscovery)
        {
            await manager.LocalPeerAnnounceAsync();
        }

        return true;
    }

    // Peers normally come from trackers/DHT; this lets a known peer be added directly.
    public async Task<bool> AddPeerAsync(string id, IPEndPoint endPoint) =>
        Find(id) is TorrentManager manager && await manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://{endPoint}")));

    // Stamps completion time and stops completed torrents that reached the seed limits; runs on a timer.
    public async Task EnforceSeedLimitsAsync()
    {
        bool changed = false;
        foreach (TorrentManager manager in _engine.Torrents.ToArray())
        {
            string id = ToId(manager.InfoHashes);
            // The snapshot can hold a torrent RemoveAsync removed meanwhile.
            if (!IsComplete(manager) || !_engine.Contains(manager.InfoHashes))
            {
                continue;
            }

            TorrentTag tag = GetTag(id);
            if (tag.CompletedAt is null)
            {
                tag = UpdateTag(id, t => t with { CompletedAt = DateTimeOffset.UtcNow });
                changed = true;
            }

            if (manager.State == TorrentState.Seeding && TorrentStatusMapping.HasReachedSeedLimit(Ratio(manager, tag), SeedingTime(tag), _options))
            {
                await manager.StopAsync();
                UpdateTag(id, t => t with { Stopped = true });
                changed = true;
            }
        }

        if (changed)
        {
            await SaveAsync();
        }
    }

    public async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            // MonoTorrent writes the path in place; a kill mid-write would leave a truncated state that blocks startup.
            string stateTempPath = EngineStatePath + ".tmp";
            await File.WriteAllBytesAsync(stateTempPath, await _engine.SaveStateAsync());
            File.Move(stateTempPath, EngineStatePath, overwrite: true);

            Dictionary<string, long> sessionUploaded = _engine.Torrents.ToDictionary(m => ToId(m.InfoHashes), m => m.Monitor.DataBytesSent);
            Dictionary<string, TorrentTag> snapshot;
            lock (_tags)
            {
                snapshot = _tags.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value with { UploadedBytes = kv.Value.UploadedBytes + sessionUploaded.GetValueOrDefault(kv.Key) });
            }

            string tempPath = TagsPath + ".tmp";
            await File.WriteAllBytesAsync(tempPath, JsonSerializer.SerializeToUtf8Bytes(snapshot));
            File.Move(tempPath, TagsPath, overwrite: true);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        try
        {
            await _seedLimitLoop;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            // Stopping writes the fast-resume data, so restart skips the full hash check.
            await _engine.StopAllAsync();
            await SaveAsync();
            _engine.Dispose();
            _stopping.Dispose();
            _saveLock.Dispose();
        }
    }

    // A corrupt state file is set aside so the app still boots; fast-resume and metadata files are separate and still load.
    private static async Task<ClientEngine?> RestoreAsync(string statePath, EngineSettings settings)
    {
        if (!File.Exists(statePath))
        {
            return null;
        }

        try
        {
            ClientEngine engine = await ClientEngine.RestoreStateAsync(statePath);
            await engine.UpdateSettingsAsync(settings);
            return engine;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            File.Move(statePath, statePath + ".corrupt", overwrite: true);
            return null;
        }
    }

    private static EngineSettings BuildSettings(TorrentEngineOptions options) => new EngineSettingsBuilder
    {
        CacheDirectory = options.StateDirectory,
        AutoSaveLoadFastResume = true,
        AutoSaveLoadMagnetLinkMetadata = true,
        AutoSaveLoadDhtCache = options.DhtEnabled,
        // shortcut: IPv4 only, add an "ipv6" endpoint when users ask for it.
        ListenEndPoints = new Dictionary<string, IPEndPoint> { ["ipv4"] = new(IPAddress.Any, options.ListenPort) },
        DhtEndPoint = options.DhtEnabled ? new IPEndPoint(IPAddress.Any, options.ListenPort) : null,
        AllowLocalPeerDiscovery = options.LocalPeerDiscoveryEnabled,
        // modularr ships as a container: UPnP/NAT-PMP would map the container port, not the host one.
        AllowPortForwarding = false,
        MaximumConnections = options.MaxConnections,
        MaximumDownloadRate = options.MaxDownloadRate,
        MaximumUploadRate = options.MaxUploadRate,
    }.ToSettings();

    // Upper-case hex like the ids Sonarr/Radarr get from qBittorrent; v2-only torrents get their v2 hash.
    private static string ToId(InfoHashes infoHashes) => infoHashes.V1OrV2.ToHex().ToUpperInvariant();

    private static TimeSpan? SeedingTime(TorrentTag tag) => DateTimeOffset.UtcNow - tag.CompletedAt;

    // shortcut: seeding time is wall-clock since completion (paused time included), track active time if users need it.
    private static double Ratio(TorrentManager manager, TorrentTag tag)
    {
        long size = TotalSize(manager);
        return size > 0 ? (double)(tag.UploadedBytes + manager.Monitor.DataBytesSent) / size : 0;
    }

    private static bool IsComplete(TorrentManager manager) => manager.HasMetadata && manager.PartialProgress >= 100;

    private static long TotalSize(TorrentManager manager) => manager.Torrent?.Size ?? manager.MagnetLink?.Size ?? 0;

    private async Task<string> AddAsync(InfoHashes infoHashes, Func<Task<TorrentManager>> add, string category)
    {
        string id = ToId(infoHashes);
        if (_engine.Contains(infoHashes))
        {
            return id;
        }

        TorrentManager manager = await add();
        lock (_tags)
        {
            _tags[id] = new TorrentTag(category, Stopped: false, CompletedAt: null, UploadedBytes: 0);
        }

        await StartCheckedAsync(manager);
        await SaveAsync();
        return id;
    }

    // No payload piece before the content check: it runs at once when the file list is known, else once the metadata arrives.
    // Every start checks again, as MonoTorrent does not keep the file priorities that skip the unwanted files.
    private async Task StartCheckedAsync(TorrentManager manager)
    {
        CancelMetadataWait(manager);
        if (manager.HasMetadata)
        {
            if (await CheckContentAsync(manager))
            {
                await manager.StartAsync();
            }
        }
        else
        {
            ContentCheckGate gate = new(new StandardPieceRequester(PieceRequesterSettings.Default));
            await manager.ChangePickerAsync(gate);
            await manager.StartAsync();
            CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
            lock (_metadataWaits)
            {
                _metadataWaits[manager] = wait;
            }

            _ = CheckOnMetadataAsync(manager, gate, wait);
        }
    }

    private void CancelMetadataWait(TorrentManager manager)
    {
        lock (_metadataWaits)
        {
            if (_metadataWaits.Remove(manager, out CancellationTokenSource? wait))
            {
                wait.Cancel();
            }
        }
    }

    private async Task CheckOnMetadataAsync(TorrentManager manager, ContentCheckGate gate, CancellationTokenSource wait)
    {
        try
        {
            await manager.WaitForMetadataAsync(wait.Token);
            if (await CheckContentAsync(manager))
            {
                gate.Open();
            }

            await SaveAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Shutdown or a failed priority change: the gate stays closed, the check runs again on the next start
            // and the stall check fails a torrent that never gets going.
        }
        finally
        {
            lock (_metadataWaits)
            {
                if (_metadataWaits.TryGetValue(manager, out CancellationTokenSource? current) && current == wait)
                {
                    _metadataWaits.Remove(manager);
                }
            }

            wait.Dispose();
        }
    }

    // A rejected torrent is stopped and reported Failed; an accepted one skips the files it does not want.
    private async Task<bool> CheckContentAsync(TorrentManager manager)
    {
        TorrentContentVerdict verdict = TorrentContentCheck.Check([.. manager.Files.Select(f => (f.Path, f.Length))], _content);
        if (verdict.Rejection is null)
        {
            foreach ((ITorrentManagerFile file, bool wanted) in manager.Files.Zip(verdict.Wanted))
            {
                if (!wanted)
                {
                    await manager.SetFilePriorityAsync(file, Priority.DoNotDownload);
                }
            }
        }
        else if (manager.State != TorrentState.Stopped)
        {
            await manager.StopAsync();
        }

        UpdateTag(ToId(manager.InfoHashes), t => t with
        {
            Rejection = verdict.Rejection,
            Stopped = t.Stopped || verdict.Rejection is not null,
        });
        return verdict.Rejection is null;
    }

    private TorrentManager? Find(string id) =>
        _engine.Torrents.FirstOrDefault(m => string.Equals(ToId(m.InfoHashes), id, StringComparison.OrdinalIgnoreCase));

    private TorrentTag GetTag(string id)
    {
        lock (_tags)
        {
            return _tags.GetValueOrDefault(id) ?? TorrentTag.Untagged;
        }
    }

    private TorrentTag UpdateTag(string id, Func<TorrentTag, TorrentTag> update)
    {
        lock (_tags)
        {
            TorrentTag tag = update(_tags.GetValueOrDefault(id) ?? TorrentTag.Untagged);
            _tags[id] = tag;
            return tag;
        }
    }

    private TorrentStatus ToStatus(TorrentManager manager)
    {
        string id = ToId(manager.InfoHashes);
        TorrentTag tag = GetTag(id);
        long totalSize = TotalSize(manager);
        // Skipped files never download, so progress and completion only count the wanted ones.
        double progress = manager.PartialProgress / 100;
        long remainingSize = (long)(totalSize * (1.0 - progress));
        long downloadRate = manager.Monitor.DownloadRate;
        double ratio = Ratio(manager, tag);
        (DownloadItemStatus status, string? message) = tag.Rejection is string rejection
            ? (DownloadItemStatus.Failed, rejection)
            : TorrentStatusMapping.Map(manager.State, IsComplete(manager), _options.DhtEnabled, manager.Error?.Exception?.Message);

        return new TorrentStatus
        {
            Id = id,
            Name = manager.Torrent?.Name ?? manager.MagnetLink?.Name ?? id,
            Category = tag.Category,
            Status = status,
            Message = message,
            Progress = progress,
            TotalSize = totalSize,
            RemainingSize = remainingSize,
            RemainingTime = TorrentStatusMapping.RemainingTime(status, remainingSize, downloadRate),
            DownloadRate = downloadRate,
            UploadRate = manager.Monitor.UploadRate,
            SeedRatio = ratio,
            SavePath = manager.SavePath,
            OutputPath = manager.Files.Count == 1 ? manager.Files[0].DownloadCompleteFullPath : manager.ContainingDirectory,
            Files = [.. manager.Files.Select(f => new TorrentFileStatus(f.Path, f.Length, f.BitField.PercentComplete / 100))],
            CanBeRemoved = TorrentStatusMapping.CanBeRemoved(manager.State, IsComplete(manager), ratio, SeedingTime(tag), _options),
            HasMetadata = manager.HasMetadata,
            Seeds = manager.Peers.Seeds,
            Peers = manager.Peers.Leechs,
            DownloadedBytes = manager.Monitor.DataBytesReceived,
        };
    }

    private async Task RunSeedLimitLoopAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(SeedLimitCheckInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await EnforceSeedLimitsAsync();
                SeedLimitError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One failed tick (disk full, a torrent removed mid-check) must not end enforcement for the whole session.
                SeedLimitError = ex;
            }
        }
    }

    // UploadedBytes holds the bytes uploaded in previous sessions; the current session's come from MonoTorrent's monitor.
    private sealed record TorrentTag(string Category, bool Stopped, DateTimeOffset? CompletedAt, long UploadedBytes)
    {
        public static readonly TorrentTag Untagged = new(string.Empty, Stopped: false, CompletedAt: null, UploadedBytes: 0);

        public string? Rejection { get; init; }
    }
}
