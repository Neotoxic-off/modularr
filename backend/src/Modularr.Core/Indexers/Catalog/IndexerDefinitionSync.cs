using System.IO.Compression;

namespace Modularr.Core.Indexers.Catalog;

// Port of Prowlarr IndexerDefinitionUpdateService.UpdateLocalDefinitions: one zip of every definition for a schema version.
public static class IndexerDefinitionSync
{
    // Newest schema CardigannDefinitionLoader reads; the server falls back to v11 for definitions with no v12 file.
    public const int SchemaVersion = 12;

    public static readonly Uri PackageUrl = new($"https://indexers.prowlarr.com/master/{SchemaVersion}/package.zip");

    // Replaces <directory> with the downloaded set, or leaves the cached set untouched; returns the failure, null on success.
    public static async Task<string?> SyncAsync(HttpClient http, string directory, CancellationToken cancellationToken)
    {
        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string staging = target + ".new";
        string backup = target + ".old";

        try
        {
            RecoverInterruptedSwap(target, backup);
            DeleteDirectory(staging);

            using HttpResponseMessage response = await http.GetAsync(PackageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using Stream package = await response.Content.ReadAsStreamAsync(cancellationToken);
            await ZipFile.ExtractToDirectoryAsync(package, staging, overwriteFiles: true, cancellationToken);

            // An empty package must not wipe a working cache.
            if (!Directory.EnumerateFiles(staging, "*.yml", SearchOption.AllDirectories).Any())
            {
                throw new InvalidDataException("the definition package holds no .yml file.");
            }

            // Two renames: Directory.Move cannot replace an existing directory.
            DeleteDirectory(backup);
            if (Directory.Exists(target))
            {
                Directory.Move(target, backup);
            }

            Directory.Move(staging, target);
            DeleteDirectory(backup);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException or UnauthorizedAccessException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            DeleteDirectoryQuietly(staging);
            return $"Indexer definition update failed: {ex.Message}";
        }
    }

    // A crash between the two renames leaves only the backup.
    private static void RecoverInterruptedSwap(string target, string backup)
    {
        if (!Directory.Exists(target) && Directory.Exists(backup))
        {
            Directory.Move(backup, target);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void DeleteDirectoryQuietly(string path)
    {
        try
        {
            DeleteDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Retried before the next sync.
        }
    }
}
