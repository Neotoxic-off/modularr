using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modularr.Core.Configuration;
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

    [Fact]
    public void MigrationsMatchTheModel()
    {
        using TestDatabase database = new();
        using ModularrDbContext db = database.NewContext();

        Assert.False(db.Database.HasPendingModelChanges());
    }
}
