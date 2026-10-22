using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace Modularr.Core.Configuration;

// Writes the folders edited in the UI back to modularr.json and reloads it, so IOptionsMonitor consumers see them at once.
public sealed class PathSettings(string configDirectory, IConfigurationRoot configuration)
{
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public async Task SaveAsync(PathOptions paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        EnsureWritableDirectory("series", paths.Series);
        EnsureWritableDirectory("movies", paths.Movies);
        EnsureWritableDirectory("anime", paths.Anime);
        EnsureWritableDirectory("downloads", paths.Downloads);

        // Only the paths section is replaced; comments in the file are lost, the other sections are kept as they are.
        string file = Path.Combine(configDirectory, ModularrOptions.FileName);
        JsonObject root = File.Exists(file)
            ? JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false), documentOptions: ReadOptions) as JsonObject ?? []
            : [];
        string key = root.Select(p => p.Key).FirstOrDefault(k => k.Equals(nameof(ModularrOptions.Paths), StringComparison.OrdinalIgnoreCase)) ?? nameof(ModularrOptions.Paths);
        root[key] = JsonSerializer.SerializeToNode(paths);

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
