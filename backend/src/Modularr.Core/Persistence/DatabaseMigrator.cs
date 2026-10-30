using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Modularr.Core.Persistence;

// Migrates in StartingAsync, which runs before any hosted service (the web server included) starts.
public sealed class DatabaseMigrator(IServiceScopeFactory scopes, DatabaseBackups backups) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ModularrDbContext db = scope.ServiceProvider.GetRequiredService<ModularrDbContext>();

        // A migration that fails halfway (or a bad one) must not cost the library: copy the database before touching it.
        if (File.Exists(db.Database.GetDbConnection().DataSource) && (await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any())
        {
            string lastApplied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).LastOrDefault() ?? "empty";
            await backups.CopyBeforeMigrationAsync(db, lastApplied, cancellationToken).ConfigureAwait(false);
        }

        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        // Persistent in the file: readers no longer block the writer (Microsoft.Data.Sqlite already retries a busy database).
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await new Accounts.AccountService(db).UnlinkVisularrAccountsAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
