using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Modularr.Core.Indexers;
using Modularr.Core.Languages;
using Modularr.Core.Downloads.Torrents;
using Modularr.Core.Library;
using Modularr.Core.Metadata;
using Modularr.Core.Notifications;
using Modularr.Core.PostProcessing;
using Modularr.Core.Subtitles;

namespace Modularr.Core.Configuration;

// Writes the settings edited in the UI back to modularr.json and reloads it, so IOptionsMonitor consumers see them at once.
public sealed class SettingsFile(string configDirectory, IConfigurationRoot configuration)
{
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    // Enums by name and no nulls, as the configuration binder reads them back.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task SavePathsAsync(PathOptions paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        EnsureWritableDirectory("series", paths.Series);
        EnsureWritableDirectory("movies", paths.Movies);
        EnsureWritableDirectory("anime", paths.Anime);
        EnsureWritableDirectory("downloads", paths.Downloads);
        await SaveSectionAsync(nameof(ModularrOptions.Paths), paths, cancellationToken).ConfigureAwait(false);
    }

    // The profile ids are checked against the database by the caller.
    public async Task SaveMediaAsync(MediaOptions media, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        media.EnsureValid();

        await SaveSectionAsync(nameof(ModularrOptions.Media), media, cancellationToken).ConfigureAwait(false);
    }

    public Task SaveSubtitlesAsync(SubtitleOptions subtitles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subtitles);
        return SaveSectionAsync(nameof(ModularrOptions.Subtitles), subtitles, cancellationToken);
    }

    public Task SaveAutomationAsync(AutomationOptions automation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(automation);
        List<ValidationResult> errors = [];
        return Validator.TryValidateObject(automation, new ValidationContext(automation), errors, validateAllProperties: true)
            ? SaveSectionAsync(nameof(ModularrOptions.Automation), automation, cancellationToken)
            : throw new ArgumentException(errors[0].ErrorMessage, nameof(automation));
    }

    public Task SaveNotificationsAsync(IReadOnlyList<NotificationConnection> notifications, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        foreach (NotificationConnection connection in notifications)
        {
            connection.EnsureValid();
        }

        return SaveSectionAsync(nameof(ModularrOptions.Notifications), notifications, cancellationToken);
    }

    // Null removes the section: the accounts are managed here again.
    public Task SaveManagedAsync(ManagedOptions? managed, CancellationToken cancellationToken)
    {
        if (managed is null)
        {
            return UpdateFileAsync(root => root.Remove(KeyIn(root, nameof(ModularrOptions.Managed))), cancellationToken);
        }

        managed.EnsureValid();
        return UpdateFileAsync(root =>
        {
            // The hook token is [JsonIgnore] so no API answer carries it: written here by hand.
            JsonObject value = JsonSerializer.SerializeToNode(managed, WriteOptions)!.AsObject();
            if (managed.HookToken is not null)
            {
                value[nameof(ManagedOptions.HookToken)] = managed.HookToken;
            }

            if (!string.IsNullOrWhiteSpace(managed.HookUrl))
            {
                value[nameof(ManagedOptions.HookUrl)] = managed.HookUrl;
            }

            root[KeyIn(root, nameof(ModularrOptions.Managed))] = value;
        }, cancellationToken);
    }

    // The state directory is not edited here: the file's value (or the default) is kept.
    public Task SaveTorrentsAsync(TorrentEngineOptions torrents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(torrents);
        List<ValidationResult> errors = [];
        if (!Validator.TryValidateObject(torrents, new ValidationContext(torrents), errors, validateAllProperties: true))
        {
            throw new ArgumentException(string.Join(" ", errors.Select(e => e.ErrorMessage)), nameof(torrents));
        }

        return UpdateFileAsync(root =>
        {
            string section = KeyIn(root, nameof(ModularrOptions.Torrents));
            JsonObject value = JsonSerializer.SerializeToNode(torrents, WriteOptions)!.AsObject();
            value.Remove(nameof(TorrentEngineOptions.StateDirectory));
            if (root[section] is JsonObject current && Find(current, nameof(TorrentEngineOptions.StateDirectory)) is JsonNode stateDirectory)
            {
                value[nameof(TorrentEngineOptions.StateDirectory)] = stateDirectory.DeepClone();
            }

            root[section] = value;
        }, cancellationToken);
    }

    public Task SaveImportModeAsync(ImportMode mode, CancellationToken cancellationToken) =>
        Enum.IsDefined(mode)
            ? SaveValueAsync(nameof(ModularrOptions.PostProcessing), nameof(PostProcessingOptions.ImportMode), mode, cancellationToken)
            : throw new ArgumentException($"Unknown import mode '{mode}'.", nameof(mode));

    // A blank recycle bin removes the key: replaced files are deleted.
    public async Task SaveImportAsync(ImportMode mode, bool renameFiles, string? recycleBinPath, CancellationToken cancellationToken)
    {
        string? recycle = string.IsNullOrWhiteSpace(recycleBinPath) ? null : recycleBinPath.Trim();
        if (recycle is not null)
        {
            EnsureWritableDirectory("recycle bin", recycle);
        }

        await SaveImportModeAsync(mode, cancellationToken).ConfigureAwait(false);
        await SaveValueAsync(nameof(ModularrOptions.PostProcessing), nameof(PostProcessingOptions.RenameFiles), renameFiles, cancellationToken).ConfigureAwait(false);
        await SaveValueAsync(nameof(ModularrOptions.PostProcessing), nameof(PostProcessingOptions.RecycleBinPath), recycle, cancellationToken).ConfigureAwait(false);
    }

    // Null or blank removes the key: movie discovery falls back to the keyless lists.
    public Task SaveTmdbApiKeyAsync(string? apiKey, CancellationToken cancellationToken)
    {
        string? key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        if (key is not null && (key.Length > 512 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')))
        {
            throw new ArgumentException("The TMDB key is a v3 API key or a v4 read access token.", nameof(apiKey));
        }

        return SaveValueAsync(nameof(ModularrOptions.Metadata), nameof(MetadataOptions.TmdbApiKey), key, cancellationToken);
    }

    // One indexer's settings, already checked against its definition (CardigannSettings.Merge); none left removes its entry.
    public Task SaveIndexerSettingsAsync(string indexerId, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexerId);
        ArgumentNullException.ThrowIfNull(values);
        return UpdateFileAsync(root =>
        {
            string sectionKey = KeyIn(root, nameof(ModularrOptions.Indexers));
            JsonObject section = root[sectionKey] as JsonObject ?? [];
            root[sectionKey] = section;
            string settingsKey = KeyIn(section, nameof(IndexerOptions.Settings));
            JsonObject settings = section[settingsKey] as JsonObject ?? [];
            section[settingsKey] = settings;
            settings.Remove(KeyIn(settings, indexerId));
            if (values.Count > 0)
            {
                settings[indexerId] = JsonSerializer.SerializeToNode(values, WriteOptions);
            }
        }, cancellationToken);
    }

    // A new key replaces the old one at once: clients still using it are refused from the next request.
    public Task SaveApiKeyAsync(string apiKey, CancellationToken cancellationToken) =>
        SaveValueAsync(nameof(ModularrOptions.Auth), nameof(AuthOptions.ApiKey), apiKey, cancellationToken);

    // The language policy became the version policy: its keys are renamed and its values mapped once, before the file is bound
    // (French -> VF, Original -> VO, Any and the other language names stay). Returns whether the file changed.
    public static bool MigrateLanguagePolicy(string configDirectory)
    {
        string file = Path.Combine(configDirectory, ModularrOptions.FileName);
        if (!File.Exists(file) || JsonNode.Parse(File.ReadAllText(file), documentOptions: ReadOptions) is not JsonObject root
            || Find(root, nameof(ModularrOptions.Media)) is not JsonObject media)
        {
            return false;
        }

        bool changed = RenameToVersion(media, "DefaultLanguage", nameof(MediaOptions.DefaultVersion));
        JsonNode?[] rules = Find(media, nameof(MediaOptions.Exceptions)) is JsonArray exceptions ? [.. exceptions] : [];
        foreach (JsonObject owner in new[] { Find(media, "Movies"), Find(media, "Series"), Find(media, "Anime") }.Concat(rules).OfType<JsonObject>())
        {
            changed |= RenameToVersion(owner, "Language", nameof(MediaDefaults.Version));
        }

        if (changed)
        {
            WriteAtomically(file, root.ToJsonString(WriteOptions));
        }

        return changed;
    }

    // The direct visularr link moved to linkarr: its old section is dropped once, before the strict binder would refuse it.
    public static bool RemoveVisularrSection(string configDirectory)
    {
        string file = Path.Combine(configDirectory, ModularrOptions.FileName);
        if (!File.Exists(file) || JsonNode.Parse(File.ReadAllText(file), documentOptions: ReadOptions) is not JsonObject root
            || !root.Remove(KeyIn(root, "Visularr")))
        {
            return false;
        }

        WriteAtomically(file, root.ToJsonString(WriteOptions));
        return true;
    }

    private static bool RenameToVersion(JsonObject owner, string from, string to)
    {
        if (owner.Select(p => p.Key).FirstOrDefault(k => k.Equals(from, StringComparison.OrdinalIgnoreCase)) is not string key)
        {
            return false;
        }

        JsonNode? value = owner[key];
        owner.Remove(key);
        owner[to] = MigratedVersion(value);
        return true;
    }

    // A language name or id; one no longer known is dropped (the default applies) rather than stopping the app at startup.
    private static string? MigratedVersion(JsonNode? value)
    {
        string? name = value is not JsonValue language ? null
            : language.TryGetValue(out string? text) ? text
            : language.TryGetValue(out int id) && Language.All.FirstOrDefault(l => l.Id == id) is Language byId ? byId.Name
            : null;
        try
        {
            return name is null ? null : ReleaseVersion.FindByName(name).Name;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static JsonNode? Find(JsonObject owner, string key) =>
        owner.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    // Only that section is replaced; comments in the file are lost, the other sections are kept as they are.
    private Task SaveSectionAsync<T>(string section, T value, CancellationToken cancellationToken) =>
        UpdateFileAsync(root => root[KeyIn(root, section)] = JsonSerializer.SerializeToNode(value, WriteOptions), cancellationToken);

    // One key of a section, so the section's other keys (and the defaults they leave out) stay as they are; null removes it.
    private Task SaveValueAsync<T>(string section, string key, T? value, CancellationToken cancellationToken) =>
        UpdateFileAsync(root =>
        {
            string sectionKey = KeyIn(root, section);
            JsonObject owner = root[sectionKey] as JsonObject ?? [];
            root[sectionKey] = owner;
            if (value is null)
            {
                owner.Remove(KeyIn(owner, key));
            }
            else
            {
                owner[KeyIn(owner, key)] = JsonSerializer.SerializeToNode(value, WriteOptions);
            }
        }, cancellationToken);

    // One save at a time: two concurrent read-modify-writes would lose one section's change.
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    private async Task UpdateFileAsync(Action<JsonObject> change, CancellationToken cancellationToken)
    {
        string file = Path.Combine(configDirectory, ModularrOptions.FileName);
        await WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JsonObject root = File.Exists(file)
                ? JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false), documentOptions: ReadOptions) as JsonObject ?? []
                : [];
            change(root);

            WriteAtomically(file, root.ToJsonString(WriteOptions));
            configuration.Reload();
        }
        finally
        {
            WriteLock.Release();
        }
    }

    // The existing spelling of a key (the binder is case-insensitive), or the key itself.
    private static string KeyIn(JsonObject owner, string key) =>
        owner.Select(p => p.Key).FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key;

    private static void WriteAtomically(string file, string json)
    {
        // Flushed to disk before the rename, so a power cut leaves the old file or the new one, never an empty one.
        string tempFile = file + ".tmp";
        using (FileStream stream = new(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
        using (StreamWriter writer = new(stream))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        File.Move(tempFile, file, overwrite: true);
    }

    private static void EnsureWritableDirectory(string field, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"{field}: '{path}' is not an absolute path.", nameof(path));
        }

        try
        {
            Directory.CreateDirectory(path);
            string probe = Path.Combine(path, $".modularr-write-test-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException($"{field}: '{path}' is not a writable directory ({ex.Message}).", nameof(path), ex);
        }
    }
}
