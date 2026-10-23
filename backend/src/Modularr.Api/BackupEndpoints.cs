using Modularr.Core.Persistence;

namespace Modularr.Api;

// Backups hold the password hashes and every key in modularr.json: admins only, the listing and downloads included.
public static class BackupEndpoints
{
    public static RouteGroupBuilder MapBackupApi(this RouteGroupBuilder api)
    {
        RouteGroupBuilder backups = api.MapGroup("/backups").RequireAuthorization(AuthEndpoints.AdminPolicy);
        backups.MapGet("/", (DatabaseBackups files) => files.List());
        backups.MapPost("/", (DatabaseBackups files, ModularrDbContext db, CancellationToken ct) => files.CreateAsync(db, ct));
        backups.MapGet("/{name}", (string name, DatabaseBackups files) =>
            files.Find(name) is string path ? Results.File(path, "application/octet-stream", name) : Results.NotFound());
        backups.MapDelete("/{name}", (string name, DatabaseBackups files) => files.Delete(name) ? Results.NoContent() : Results.NotFound());
        return api;
    }
}
