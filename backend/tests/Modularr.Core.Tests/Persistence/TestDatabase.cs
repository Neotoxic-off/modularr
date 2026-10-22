using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modularr.Core.Persistence;

namespace Modularr.Core.Tests.Persistence;

// A migrated in-memory SQLite database that lives as long as the test; each NewContext() starts with an empty change tracker.
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");

    public TestDatabase()
    {
        _connection.Open();
        using ModularrDbContext db = NewContext();
        db.Database.Migrate();
    }

    public ModularrDbContext NewContext() => new(new DbContextOptionsBuilder<ModularrDbContext>().UseSqlite(_connection).Options);

    // Inserts the entities in one context and returns them with their generated ids.
    public T[] Insert<T>(params T[] entities)
        where T : class
    {
        using ModularrDbContext db = NewContext();
        db.AddRange(entities);
        db.SaveChanges();
        return entities;
    }

    public Series AddSeries(int tvdbId) =>
        Insert(new Series { TvdbId = tvdbId, Title = $"Series {tvdbId}", SortTitle = $"series {tvdbId}", Path = $"/tv/{tvdbId}", QualityProfileId = 1 })[0];

    public Movie AddMovie(int tmdbId) =>
        Insert(new Movie { TmdbId = tmdbId, Title = $"Movie {tmdbId}", Path = $"/movies/{tmdbId}", QualityProfileId = 1 })[0];

    public void Dispose() => _connection.Dispose();
}
