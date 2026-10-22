using Microsoft.EntityFrameworkCore;
using Modularr.Core.Activities;
using Modularr.Core.Persistence;
using Modularr.Core.Tests.Persistence;

namespace Modularr.Core.Tests.Activities;

public sealed class ActivityLogTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AnActivityIsPublishedOnlyOnceSaved()
    {
        ActivityFeed feed = new();
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using IAsyncEnumerator<ServerEvent?> events = feed.SubscribeAsync(TimeSpan.FromMinutes(1), ct).GetAsyncEnumerator(ct);
        ValueTask<bool> next = events.MoveNextAsync();
        using ModularrDbContext db = _database.NewContext();
        ActivityLog log = new(db, feed);

        log.Add(new Activity { Kind = ActivityKind.Added, Level = ActivityLevel.Info, Title = "Show" });
        Assert.False(next.IsCompleted);
        await db.SaveChangesAsync(ct);

        Assert.True(await next);
        Activity published = Assert.IsType<Activity>(events.Current!.Data);
        Assert.Equal(("activity", "Show"), (events.Current.Type, published.Title));
        Assert.True(published.Id > 0);
    }

    [Fact]
    public async Task ListPagesNewestFirstAndReadMarksUpToAnId()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Movie movie = _database.AddMovie(1);
        int[] ids = [.. _database.Insert([.. Enumerable.Range(1, 5).Select(i => New(Now, movieId: i % 2 == 0 ? movie.Id : null))]).Select(a => a.Id)];
        using ModularrDbContext db = _database.NewContext();
        ActivityLog log = new(db, new ActivityFeed());

        Assert.Equal([ids[4], ids[3]], (await log.ListAsync(before: null, limit: 2, null, null, ct)).Select(a => a.Id));
        Assert.Equal([ids[2], ids[1]], (await log.ListAsync(before: ids[3], limit: 2, null, null, ct)).Select(a => a.Id));
        Assert.Equal([ids[3], ids[1]], (await log.ListAsync(before: null, limit: 50, null, movie.Id, ct)).Select(a => a.Id));

        await log.MarkReadAsync(ids[2], ct);
        Assert.Equal(2, await log.UnreadCountAsync(ct));
        await log.MarkReadAsync(upTo: null, ct);
        Assert.Equal(0, await log.UnreadCountAsync(ct));
    }

    [Fact]
    public async Task PruneDropsOldActivitiesAndKeepsTheNewestThousand()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _database.Insert(New(Now - ActivityLog.MaxAge - TimeSpan.FromMinutes(1)));
        Activity[] recent = _database.Insert([.. Enumerable.Range(0, ActivityLog.Keep + 5).Select(_ => New(Now))]);
        using ModularrDbContext db = _database.NewContext();

        Assert.Equal(6, await new ActivityLog(db, new ActivityFeed()).PruneAsync(Now, ct));

        Assert.Equal(recent[5..].Select(a => a.Id), await db.Activities.OrderBy(a => a.Id).Select(a => a.Id).ToListAsync(ct));
    }

    private static Activity New(DateTime time, int? movieId = null) =>
        new() { Time = time, Kind = ActivityKind.Grabbed, Level = ActivityLevel.Success, Title = "Release", MovieId = movieId };
}
