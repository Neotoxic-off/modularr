using Microsoft.Extensions.Logging;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modularr.Core.Configuration;

namespace Modularr.Core.Persistence;

public sealed record BackupFile(string Name, long Size, DateTime Created);

// <config>/backups: the daily (or admin-made) zips of the database and modularr.json, and the copies taken before a migration.
public sealed class DatabaseBackups(string configDirectory)
{
    public const int ArchivesToKeep = 7;
    public const int PreMigrationCopiesToKeep = 5;
    private const string ArchivePattern = "modularr-*.zip";
    private const string PreMigrationPattern = "modularr-pre-*.db";

    public string Folder { get; } = Path.Combine(configDirectory, "backups");

    public IReadOnlyList<BackupFile> List() => !Directory.Exists(Folder) ? [] :
        [.. new DirectoryInfo(Folder).EnumerateFiles(ArchivePattern).Concat(new DirectoryInfo(Folder).EnumerateFiles(PreMigrationPattern))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new BackupFile(f.Name, f.Length, f.LastWriteTimeUtc))];

    // The name must be one the listing shows: anything else (a path, "..") is refused, so no file outside the folder is reachable.
    public string? Find(string name) =>
        List().Any(b => string.Equals(b.Name, name, StringComparison.Ordinal)) ? Path.Combine(Folder, name) : null;

    public bool Delete(string name)
    {
        if (Find(name) is not string path)
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    // VACUUM INTO writes a consistent copy (WAL content included) while the app keeps running.
    public async Task<BackupFile> CreateAsync(ModularrDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        Directory.CreateDirectory(Folder);
        string name = $"modularr-{DateTime.UtcNow:yyyyMMdd-HHmm}.zip";
        string databaseCopy = Path.Combine(Folder, $"{Guid.NewGuid():N}.db.tmp");
        string archiveTemp = Path.Combine(Folder, $"{Guid.NewGuid():N}.zip.tmp");
        try
        {
            await db.Database.ExecuteSqlAsync($"VACUUM INTO {databaseCopy}", cancellationToken).ConfigureAwait(false);
            using (ZipArchive zip = ZipFile.Open(archiveTemp, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(databaseCopy, "modularr.db");
                string settings = Path.Combine(configDirectory, ModularrOptions.FileName);
                if (File.Exists(settings))
                {
                    zip.CreateEntryFromFile(settings, ModularrOptions.FileName);
                }
            }

            File.Move(archiveTemp, Path.Combine(Folder, name), overwrite: true);
        }
        finally
        {
            File.Delete(databaseCopy);
            File.Delete(archiveTemp);
        }

        Prune(Folder, ArchivePattern, ArchivesToKeep);
        return List().First(b => b.Name == name);
    }

    public async Task CopyBeforeMigrationAsync(ModularrDbContext db, string lastAppliedMigration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, $"modularr-pre-{lastAppliedMigration}-{DateTime.UtcNow:yyyyMMddHHmmss}.db");
        await db.Database.ExecuteSqlAsync($"VACUUM INTO {path}", cancellationToken).ConfigureAwait(false);
        Prune(Folder, PreMigrationPattern, PreMigrationCopiesToKeep);
    }

    // Keeps the newest files matching the pattern, deletes the rest.
    public static void Prune(string folder, string pattern, int keep)
    {
        foreach (FileInfo old in new DirectoryInfo(folder).EnumerateFiles(pattern).OrderByDescending(f => f.LastWriteTimeUtc).Skip(keep))
        {
            old.Delete();
        }
    }
}

// Backs up once a day: checks hourly whether the newest zip is a day old, so frequent restarts do not skip it.
public sealed class DatabaseBackupScheduler(IServiceScopeFactory scopes, DatabaseBackups backups, ILogger<DatabaseBackupScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            if (backups.List().Any(b => b.Name.EndsWith(".zip", StringComparison.Ordinal) && b.Created > DateTime.UtcNow.AddDays(-1)))
            {
                continue;
            }

            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                await backups.CreateAsync(scope.ServiceProvider.GetRequiredService<ModularrDbContext>(), stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.JobFailed("Nightly backup", ex);
                // An exception escaping ExecuteAsync stops the host; a full disk or busy database is retried on the next tick.
            }
        }
    }
}
