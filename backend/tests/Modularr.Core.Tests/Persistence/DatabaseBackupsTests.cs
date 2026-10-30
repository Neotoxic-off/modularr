using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modularr.Core.Configuration;
using Modularr.Core.Persistence;

namespace Modularr.Core.Tests.Persistence;

public sealed class DatabaseBackupsTests : IDisposable
{
    private readonly string _configDirectory = Directory.CreateTempSubdirectory("modularr-backup-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // pooled connections keep the file locked on Windows
        Directory.Delete(_configDirectory, recursive: true);
    }

    private string Backups => Path.Combine(_configDirectory, "backups");

    [Fact]
    public void PruneKeepsTheNewestFiles()
    {
        Directory.CreateDirectory(Backups);
        for (int day = 1; day <= 9; day++)
        {
            string path = Path.Combine(Backups, $"modularr-202601{day:00}-0300.zip");
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, day, 3, 0, 0, DateTimeKind.Utc));
        }

        File.WriteAllText(Path.Combine(Backups, "modularr-pre-Init-20250101000000.db"), "x");
        File.SetLastWriteTimeUtc(Path.Combine(Backups, "modularr-pre-Init-20250101000000.db"), new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        DatabaseBackups.Prune(Backups, "modularr-*.zip", 7);

        Assert.Equal(
            ["modularr-20260109-0300.zip", "modularr-20260108-0300.zip", "modularr-20260107-0300.zip", "modularr-20260106-0300.zip",
             "modularr-20260105-0300.zip", "modularr-20260104-0300.zip", "modularr-20260103-0300.zip", "modularr-pre-Init-20250101000000.db"],
            new DatabaseBackups(_configDirectory).List().Select(b => b.Name));
    }

    [Theory]
    [InlineData("../modularr.json")]
    [InlineData("..\\modularr.json")]
    [InlineData("modularr.json")]
    [InlineData("")]
    [InlineData("MODULARR-20260101-0300.ZIP")]
    public void FindRefusesNamesNotInTheListing(string name)
    {
        Directory.CreateDirectory(Backups);
        File.WriteAllText(Path.Combine(_configDirectory, "modularr.json"), "{}");
        File.WriteAllText(Path.Combine(Backups, "modularr-20260101-0300.zip"), "x");
        DatabaseBackups backups = new(_configDirectory);

        Assert.Null(backups.Find(name));
        Assert.False(backups.Delete(name));
        Assert.Equal(Path.Combine(Backups, "modularr-20260101-0300.zip"), backups.Find("modularr-20260101-0300.zip"));
    }

    [Fact]
    public async Task CreateZipsTheDatabaseAndTheSettings()
    {
        File.WriteAllText(Path.Combine(_configDirectory, ModularrOptions.FileName), "{}");
        await using ServiceProvider provider = new ServiceCollection().AddModularrCore(_configDirectory).BuildServiceProvider();
        await Migrator(provider).StartingAsync(TestContext.Current.CancellationToken);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        BackupFile backup = await provider.GetRequiredService<DatabaseBackups>()
            .CreateAsync(scope.ServiceProvider.GetRequiredService<ModularrDbContext>(), TestContext.Current.CancellationToken);

        using ZipArchive zip = ZipFile.OpenRead(Path.Combine(Backups, backup.Name));
        Assert.Equal(["modularr.db", "modularr.json"], zip.Entries.Select(e => e.FullName).Order());
        Assert.Single(Directory.GetFiles(Backups));
    }

    [Fact]
    public async Task MigrationCopiesTheDatabaseOnlyWhenMigrationsArePending()
    {
        await using ServiceProvider provider = new ServiceCollection().AddModularrCore(_configDirectory).BuildServiceProvider();

        // A new database: nothing to copy.
        await Migrator(provider).StartingAsync(TestContext.Current.CancellationToken);
        Assert.False(Directory.Exists(Backups));

        // Up to date: nothing to copy.
        await Migrator(provider).StartingAsync(TestContext.Current.CancellationToken);
        Assert.False(Directory.Exists(Backups));

        // An older database: copied before the pending migrations run.
        SqliteConnection.ClearAllPools();
        foreach (string file in Directory.GetFiles(_configDirectory, "modularr.db*"))
        {
            File.Delete(file);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ModularrDbContext>().GetService<IMigrator>().Migrate("20261022201204_Activities");
        }

        await Migrator(provider).StartingAsync(TestContext.Current.CancellationToken);
        string copy = Assert.Single(Directory.GetFiles(Backups));
        Assert.StartsWith("modularr-pre-20261022201204_Activities-", Path.GetFileName(copy), StringComparison.Ordinal);
    }

    private static DatabaseMigrator Migrator(IServiceProvider provider) => provider.GetServices<IHostedService>().OfType<DatabaseMigrator>().Single();
}
