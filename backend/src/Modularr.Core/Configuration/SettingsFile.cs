using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Modularr.Core.Library;

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

    // Only that section is replaced; comments in the file are lost, the other sections are kept as they are.
    private async Task SaveSectionAsync<T>(string section, T value, CancellationToken cancellationToken)
    {
        string file = Path.Combine(configDirectory, ModularrOptions.FileName);
        JsonObject root = File.Exists(file)
            ? JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false), documentOptions: ReadOptions) as JsonObject ?? []
            : [];
        string key = root.Select(p => p.Key).FirstOrDefault(k => k.Equals(section, StringComparison.OrdinalIgnoreCase)) ?? section;
        root[key] = JsonSerializer.SerializeToNode(value, WriteOptions);

        string tempFile = file + ".tmp";
        await File.WriteAllTextAsync(tempFile, root.ToJsonString(WriteOptions), cancellationToken).ConfigureAwait(false);
        File.Move(tempFile, file, overwrite: true);
        configuration.Reload();
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
