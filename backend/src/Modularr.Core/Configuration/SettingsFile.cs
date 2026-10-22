using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Modularr.Core.Languages;
using Modularr.Core.Library;
using Modularr.Core.Metadata;
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

    public Task SaveImportModeAsync(ImportMode mode, CancellationToken cancellationToken) =>
        Enum.IsDefined(mode)
            ? SaveValueAsync(nameof(ModularrOptions.PostProcessing), nameof(PostProcessingOptions.ImportMode), mode, cancellationToken)
            : throw new ArgumentException($"Unknown import mode '{mode}'.", nameof(mode));

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

    private async Task UpdateFileAsync(Action<JsonObject> change, CancellationToken cancellationToken)
    {
        string file = Path.Combine(configDirectory, ModularrOptions.FileName);
        JsonObject root = File.Exists(file)
            ? JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false), documentOptions: ReadOptions) as JsonObject ?? []
            : [];
        change(root);

        WriteAtomically(file, root.ToJsonString(WriteOptions));
        configuration.Reload();
    }

    // The existing spelling of a key (the binder is case-insensitive), or the key itself.
    private static string KeyIn(JsonObject owner, string key) =>
        owner.Select(p => p.Key).FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key;

    private static void WriteAtomically(string file, string json)
    {
        string tempFile = file + ".tmp";
        File.WriteAllText(tempFile, json);
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
