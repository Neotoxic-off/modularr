using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modularr.Core.Configuration;
using Modularr.Core.Languages;
using Modularr.Core.Persistence;

namespace Modularr.Core.Tests.Persistence;

public sealed class MigrationTests : IDisposable
{
    private readonly string _configDirectory = Directory.CreateTempSubdirectory("modularr-db-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // pooled connections keep the file locked on Windows
        Directory.Delete(_configDirectory, recursive: true);
    }

    [Fact]
    public async Task StartupMigratesAnEmptyDatabaseInTheConfigDirectory()
    {
        await using ServiceProvider provider = new ServiceCollection().AddModularrCore(_configDirectory).BuildServiceProvider();
        DatabaseMigrator migrator = provider.GetServices<IHostedService>().OfType<DatabaseMigrator>().Single();

        await migrator.StartingAsync(TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(_configDirectory, "modularr.db")));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ModularrDbContext db = scope.ServiceProvider.GetRequiredService<ModularrDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(6, await db.QualityProfiles.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void DatabasePathIsConfigurable()
    {
        File.WriteAllText(Path.Combine(_configDirectory, ModularrOptions.FileName), """{ "Database": { "Path": "data/library.db" } }""");
        using ServiceProvider provider = new ServiceCollection().AddModularrCore(_configDirectory).BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        string? dataSource = scope.ServiceProvider.GetRequiredService<ModularrDbContext>().Database.GetDbConnection().DataSource;

        Assert.Equal(Path.Combine(_configDirectory, "data", "library.db"), dataSource);
    }

    // French -> VF, Original -> VO, Any and the other languages keep their name; no override stays none.
    [Fact]
    public void TheLanguageOverrideBecomesTheVersionOverride()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        using ModularrDbContext db = new(new DbContextOptionsBuilder<ModularrDbContext>().UseSqlite(connection).Options);
        IMigrator migrator = db.GetService<IMigrator>();
        migrator.Migrate("20261022201204_Activities");
        using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO Movies (TmdbId, Title, Year, Runtime, Monitored, Path, QualityProfileId, Language) VALUES
                (1, 'a', 0, 0, 1, '/a', 1, 2), (2, 'b', 0, 0, 1, '/b', 1, -2), (3, 'c', 0, 0, 1, '/c', 1, -1), (4, 'd', 0, 0, 1, '/d', 1, 4),
                (5, 'e', 0, 0, 1, '/e', 1, NULL);
                """;
            insert.ExecuteNonQuery();
        }

        migrator.Migrate();

        Assert.Equal(
            [ReleaseVersion.VF, ReleaseVersion.VO, ReleaseVersion.Any, ReleaseVersion.FindByName("German"), null],
            db.Movies.OrderBy(m => m.TmdbId).Select(m => m.Version).ToList());
    }

    [Fact]
    public void MigrationsMatchTheModel()
    {
        using TestDatabase database = new();
        using ModularrDbContext db = database.NewContext();

        Assert.False(db.Database.HasPendingModelChanges());
    }
}
