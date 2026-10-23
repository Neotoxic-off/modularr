using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Modularr.Core.Configuration;
using Modularr.Core.PostProcessing;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.PiecePicking;
using MonoTorrent.Trackers;

namespace Modularr.Core.Downloads.Torrents;

// In-process BitTorrent client owning one MonoTorrent ClientEngine; replaces the external qBittorrent of Sonarr/Radarr.
public sealed class TorrentEngine : IAsyncDisposable
{
    private const string EngineStateFile = "engine.state";

    // MonoTorrent's own HTTP (tracker announces, web seeds) goes through the guard every remote-chosen URL uses: public addresses
    // only, checked at connection time (no DNS rebinding), and no redirects (each hop would escape the check).
    private static readonly Factories GuardedFactories = Factories.Default.WithHttpClientCreator(_ =>
        new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectCallback = PublicAddress.ConnectAsync }));
    private const string TagsFile = "torrents.json";

    private static readonly TimeSpan SeedLimitCheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StopWaitTimeout = TimeSpan.FromSeconds(30);

    // The stop announce otherwise waits on every tracker (100+ public ones added): a remove or pause took minutes.
    private static readonly TimeSpan StopAnnounceTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StopPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly ClientEngine _engine;
    private TorrentEngineOptions _options;
    private readonly PostProcessingOptions _content;
    private readonly Dictionary<string, TorrentTag> _tags;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    // MonoTorrent starts its listeners, DHT and per-tick work with the first torrent and stops them with the last one, across awaits:
    // a torrent stopping while another starts left the engine "running" with DHT and listeners shut (no peer until a restart).
    // Every torrent start and stop goes through this lock, so the two never interleave.
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();

    // One metadata wait per magnet: pause, remove and every new start cancel the previous one.
    private readonly Dictionary<TorrentManager, CancellationTokenSource> _metadataWaits = [];
    private readonly Task _seedLimitLoop;

    // Extra trackers (PublicTrackers) given to every public torrent; swapped whole when the list is refreshed.
    private IReadOnlyList<Uri> _publicTrackers = [];

    // Watchdog: when a magnet waits for metadata with no peer while the whole engine reaches nobody, its torrent is restarted.
    private static readonly TimeSpan IdleRestartAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IdleRestartEvery = TimeSpan.FromMinutes(10);
    private readonly Dictionary<string, DateTimeOffset> _idleSince = [];
    private readonly Dictionary<string, DateTimeOffset> _idleRestartedAt = [];
    private readonly ILogger _logger;

    private TorrentEngine(ClientEngine engine, TorrentEngineOptions options, PostProcessingOptions content, Dictionary<string, TorrentTag> tags, ILogger logger)
    {
        _engine = engine;
        _options = options;
        _content = content;
        _tags = tags;
        _logger = logger;
        _seedLimitLoop = RunSeedLimitLoopAsync(_stopping.Token);
    }

    private string EngineStatePath => Path.Combine(_options.StateDirectory, EngineStateFile);

    private string TagsPath => Path.Combine(_options.StateDirectory, TagsFile);

    // Rate, connection and seed limits apply at once; the state directory, listen port, DHT and local peer discovery need a restart.
    public async Task ApplyAsync(TorrentEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options with
        {
            StateDirectory = _options.StateDirectory,
            ListenPort = _options.ListenPort,
            DhtEnabled = _options.DhtEnabled,
            LocalPeerDiscoveryEnabled = _options.LocalPeerDiscoveryEnabled,
        };
        await _engine.UpdateSettingsAsync(BuildSettings(_options));
    }

    // Whether MonoTorrent runs its listeners, DHT and per-tick work (announces, connections); false once every torrent stopped.
    public bool IsRunning => _engine.IsRunning;

    public int ListenPort => _options.ListenPort;

    // Peer connections across every torrent: zero while torrents are active means the engine reaches nobody.
    public int OpenConnections => _engine.ConnectionManager.OpenConnections;

    // Last failure of the background seed-limit check, null once a check succeeds again.
    public Exception? SeedLimitError { get; private set; }

    // The content check judges each torrent's file list with the post-processing extension lists.
    public static async Task<TorrentEngine> StartAsync(TorrentEngineOptions options, PostProcessingOptions? content = null, ILogger? logger = null)
    {
        Directory.CreateDirectory(options.StateDirectory);
        string statePath = Path.Combine(options.StateDirectory, EngineStateFile);
        string tagsPath = Path.Combine(options.StateDirectory, TagsFile);
        EngineSettings settings = BuildSettings(options);

        ClientEngine engine = await RestoreAsync(statePath, settings) ?? new ClientEngine(settings, GuardedFactories);

        Dictionary<string, TorrentTag> tags = File.Exists(tagsPath)
            ? JsonSerializer.Deserialize<Dictionary<string, TorrentTag>>(await File.ReadAllBytesAsync(tagsPath)) ?? []
            : [];

        TorrentEngine started = new(engine, options, content ?? new PostProcessingOptions(), tags, logger ?? NullLogger.Instance);
        foreach (TorrentManager manager in engine.Torrents)
        {
            await RemoveUnsafeTrackersAsync(manager);
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

        return await AddAsync(magnet.InfoHashes, () => _engine.AddAsync(magnet, OwnDirectory(saveDirectory, magnet.InfoHashes)), category);
    }

    public async Task<string> AddTorrentFileAsync(ReadOnlyMemory<byte> torrentFile, string saveDirectory, string category)
    {
        Torrent torrent = Torrent.Load(torrentFile.Span);
        return await AddAsync(torrent.InfoHashes, () => _engine.AddAsync(torrent, OwnDirectory(saveDirectory, torrent.InfoHashes)), category);
    }

    // New torrents get the list when added; the ones already in the engine get it now (MonoTorrent does not persist added trackers).
    public async Task SetPublicTrackersAsync(IReadOnlyList<Uri> trackers)
    {
        _publicTrackers = trackers;
        foreach (TorrentManager manager in _engine.Torrents.ToList())
        {
            await AddPublicTrackersAsync(manager);
        }
    }

    // Trackers come from indexer magnets and .torrent files: one on the local network would make the engine announce there (SSRF),
    // so only udp/http/https trackers whose host is public by name and by every resolved address are kept.
    private static async Task RemoveUnsafeTrackersAsync(TorrentManager manager)
    {
        // A private torrent's tracker list cannot be changed (MonoTorrent throws); its announces still go through GuardedFactories.
        if (manager.Torrent?.IsPrivate == true)
        {
            return;
        }

        foreach (ITracker tracker in manager.TrackerManager.Tiers.SelectMany(tier => tier.Trackers).ToList())
        {
            if (!await PublicAddress.IsPublicTrackerAsync(tracker.Uri))
            {
                await manager.TrackerManager.RemoveTrackerAsync(tracker);
            }
        }
    }

    // Never on a private torrent: its tracker must be the only one, and a magnet's privacy is only known once its metadata arrives.
    private async Task AddPublicTrackersAsync(TorrentManager manager)
    {
        // A magnet waits for its metadata (CheckOnMetadataAsync): until then it may be private, and announcing it would leak it.
        if (manager.Torrent is not { IsPrivate: false })
        {
            return;
        }

        HashSet<string> present = [.. manager.TrackerManager.Tiers.SelectMany(tier => tier.Trackers).Select(tracker => tracker.Uri.AbsoluteUri)];
        foreach (Uri tracker in _publicTrackers.Where(t => !present.Contains(t.AbsoluteUri)))
        {
            await manager.TrackerManager.AddTrackerAsync(tracker);
        }
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
        await StopAsync(manager);
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
        await StopAsync(manager);
        await UnderSaveLockAsync(() => _engine.RemoveAsync(manager, deleteData ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.CacheDataOnly));
        DeleteOwnDirectory(manager.SavePath, ToId(manager.InfoHashes), deleteData);
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

        // MonoTorrent keeps the metadata size the first peer announced, even a wrong one, until the torrent restarts: a magnet
        // stalled without metadata is restarted so one hostile peer cannot block it (and get a good release blocklisted).
        if (!manager.HasMetadata)
        {
            await StopAsync(manager);
            await StartCheckedAsync(manager);
            return true;
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
                await StopAsync(manager);
                UpdateTag(id, t => t with { Stopped = true });
                changed = true;
            }
        }

        if (changed)
        {
            await SaveAsync();
        }
    }

    // A magnet with no metadata and no peer for 5 min while the engine has no connection at all is stuck, not just rare: its torrent
    // is stopped and started again (which also restarts MonoTorrent's DHT and listeners if they were down), at most every 10 min.
    // Returns how many were restarted; runs on the seed-limit timer.
    public async Task<int> RestartIdleMagnetsAsync(DateTimeOffset now)
    {
        bool engineIdle = _engine.ConnectionManager.OpenConnections == 0;
        HashSet<string> idle = [];
        int restarted = 0;
        foreach (TorrentManager manager in _engine.Torrents.ToArray())
        {
            if (!engineIdle || manager.HasMetadata || manager.State != TorrentState.Metadata || manager.OpenConnections > 0)
            {
                continue;
            }

            string id = ToId(manager.InfoHashes);
            idle.Add(id);
            DateTimeOffset since = _idleSince.TryAdd(id, now) ? now : _idleSince[id];
            if (now - since < IdleRestartAfter || (_idleRestartedAt.TryGetValue(id, out DateTimeOffset last) && now - last < IdleRestartEvery))
            {
                continue;
            }

            _logger.RestartingIdleTorrent(manager.Torrent?.Name ?? manager.MagnetLink?.Name ?? id, now - since);
            _idleRestartedAt[id] = now;
            _idleSince[id] = now;
            await ReannounceAsync(id);
            restarted++;
        }

        foreach (string gone in _idleSince.Keys.Except(idle).ToList())
        {
            _idleSince.Remove(gone);
        }

        foreach (string old in _idleRestartedAt.Where(kv => now - kv.Value >= IdleRestartEvery).Select(kv => kv.Key).ToList())
        {
            _idleRestartedAt.Remove(old);
        }

        return restarted;
    }

    // SaveStateAsync walks the engine's torrent list across awaits: adding or removing one meanwhile threw
    // "Collection was modified". Both go through the save lock so they never overlap a save.
    private async Task<T> UnderSaveLockAsync<T>(Func<Task<T>> change)
    {
        await _saveLock.WaitAsync();
        try
        {
            return await change();
        }
        finally
        {
            _saveLock.Release();
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

    // MonoTorrent writes fast-resume only on stop or when seeding starts: after a crash every partial download would be hash
    // checked from scratch and its upload counters lost. Saving them while running keeps the loss to the last few minutes.
    public async Task CheckpointAsync()
    {
        foreach (TorrentManager manager in _engine.Torrents.ToArray())
        {
            if (!manager.HasMetadata || manager.State is not (TorrentState.Downloading or TorrentState.Seeding))
            {
                continue;
            }

            try
            {
                FastResume fastResume = await manager.SaveFastResumeAsync();
                // Flushed after the capture: every piece the checkpoint calls done is then on disk, not in the write cache.
                await _engine.DiskManager.FlushAsync(manager);
                string path = _engine.Settings.GetFastResumePath(manager.InfoHashes);
                string tempPath = path + ".tmp";
                await File.WriteAllBytesAsync(tempPath, fastResume.Encode());
                File.Move(tempPath, path, overwrite: true);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // One torrent removed or stopped meanwhile must not cost the others their checkpoint.
            }
        }

        await SaveAsync();
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
            await ExclusiveAsync(_engine.StopAllAsync);
            await SaveAsync();
            _engine.Dispose();
            _stopping.Dispose();
            _saveLock.Dispose();
            _lifecycle.Dispose();
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
            ClientEngine engine = await ClientEngine.RestoreStateAsync(statePath, GuardedFactories);
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
    // Each torrent downloads into a folder of its own named by its info hash: whatever names a remote torrent picks, it can never
    // write, import or delete another download's files.
    private static string OwnDirectory(string saveDirectory, InfoHashes infoHashes) => Path.Combine(saveDirectory, ToId(infoHashes));

    // The torrent's own folder goes with it: everything when its data is deleted (extracted archives included), otherwise only if
    // the import left it empty. Torrents added before per-torrent folders have a shared save path, which is never touched.
    private static void DeleteOwnDirectory(string savePath, string id, bool deleteData)
    {
        try
        {
            DirectoryInfo own = new(savePath);
            if (!own.Exists || !string.Equals(own.Name, id, StringComparison.OrdinalIgnoreCase) || own.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return;
            }

            if (deleteData || !own.EnumerateFileSystemInfos().Any())
            {
                own.Delete(recursive: deleteData);
            }
        }
        catch (IOException)
        {
            // In use or already gone: the folder is left for the user, the torrent is removed either way.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string ToId(InfoHashes infoHashes) => infoHashes.V1OrV2.ToHex().ToUpperInvariant();

    private static TimeSpan? SeedingTime(TorrentTag tag) => DateTimeOffset.UtcNow - tag.CompletedAt;

    // shortcut: seeding time is wall-clock since completion (paused time included), track active time if users need it.
    private static double Ratio(TorrentManager manager, TorrentTag tag)
    {
        long size = TotalSize(manager);
        return size > 0 ? (double)(tag.UploadedBytes + manager.Monitor.DataBytesSent) / size : 0;
    }

    private static bool IsComplete(TorrentManager manager) => manager.HasMetadata && manager.PartialProgress >= 100;

    // MonoTorrent throws when asked to stop a manager that is stopped or already stopping (a slow tracker stop announce).
    private async Task ExclusiveAsync(Func<Task> startOrStop)
    {
        await _lifecycle.WaitAsync();
        try
        {
            await startOrStop();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task StopAsync(TorrentManager manager)
    {
        if (manager.State == TorrentState.Stopping)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow + StopWaitTimeout;
            while (manager.State == TorrentState.Stopping && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(StopPollInterval);
            }

            if (manager.State == TorrentState.Stopping)
            {
                throw new InvalidOperationException("The torrent is still stopping, try again in a moment.");
            }
        }

        if (manager.State != TorrentState.Stopped)
        {
            await ExclusiveAsync(() => manager.StopAsync(StopAnnounceTimeout));
        }
    }

    private static long TotalSize(TorrentManager manager) => manager.Torrent?.Size ?? manager.MagnetLink?.Size ?? 0;

    private async Task<string> AddAsync(InfoHashes infoHashes, Func<Task<TorrentManager>> add, string category)
    {
        string id = ToId(infoHashes);
        if (_engine.Contains(infoHashes))
        {
            return id;
        }

        TorrentManager manager = await UnderSaveLockAsync(add);
        lock (_tags)
        {
            _tags[id] = new TorrentTag(category, Stopped: false, CompletedAt: null, UploadedBytes: 0);
        }

        await RemoveUnsafeTrackersAsync(manager);
        await AddPublicTrackersAsync(manager);
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
                await ExclusiveAsync(manager.StartAsync);
            }
        }
        else
        {
            ContentCheckGate gate = new(new StandardPieceRequester(PieceRequesterSettings.Default));
            await manager.ChangePickerAsync(gate);
            await ExclusiveAsync(manager.StartAsync);
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
            await AddPublicTrackersAsync(manager);
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
        string? rejection = verdict.Rejection ?? FolderRejection(manager);
        if (rejection is null)
        {
            foreach ((ITorrentManagerFile file, bool wanted) in manager.Files.Zip(verdict.Wanted))
            {
                if (!wanted)
                {
                    await manager.SetFilePriorityAsync(file, Priority.DoNotDownload);
                }
            }
        }
        else
        {
            await StopAsync(manager);
        }

        UpdateTag(ToId(manager.InfoHashes), t => t with
        {
            Rejection = rejection,
            Stopped = t.Stopped || rejection is not null,
        });
        return rejection is null;
    }

    // Post-processing walks the torrent's folder (or file): it must be the torrent's own, strictly inside the category folder.
    // A name like ".", "" or "x/.." would make it the shared category folder, and a name already used by another torrent would
    // make it that torrent's folder; either way the import would delete, extract or move other downloads' files.
    private string? FolderRejection(TorrentManager manager)
    {
        static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string save = Full(manager.SavePath);
        string own = manager.Files.Count == 1 ? Full(manager.Files[0].FullPath) : Full(manager.ContainingDirectory);
        bool inside = own.StartsWith(save + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && (manager.Files.Count == 1 ? Path.GetDirectoryName(own) is string parent && Full(parent) == save : true);
        bool shared = manager.Files.Count > 1 && _engine.Torrents.Any(other => other != manager && other.HasMetadata && other.Files.Count > 1
            && string.Equals(Full(other.ContainingDirectory), own, StringComparison.OrdinalIgnoreCase));
        return inside && !shared ? null : "Torrent name does not give it its own folder (\".\", empty, \"..\" or shared with another download)";
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
        DateTimeOffset lastCheckpoint = DateTimeOffset.UtcNow;
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

            try
            {
                await RestartIdleMagnetsAsync(DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.JobFailed("Idle torrent watchdog", ex);
            }

            if (DateTimeOffset.UtcNow - lastCheckpoint >= CheckpointInterval)
            {
                lastCheckpoint = DateTimeOffset.UtcNow;
                try
                {
                    await CheckpointAsync();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A full disk: the shutdown save and the next checkpoint try again.
                }
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

internal static partial class TorrentEngineLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Torrent {Name} has had no metadata, no peer and an engine with no connection for {Idle}; restarting it")]
    public static partial void RestartingIdleTorrent(this ILogger logger, string name, TimeSpan idle);
}
