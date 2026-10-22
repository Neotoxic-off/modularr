using Microsoft.EntityFrameworkCore;
using Modularr.Core.Persistence;

namespace Modularr.Core.Activities;

// Records activities in the caller's unit of work and publishes each one to the live feed once it is saved.
public sealed class ActivityLog
{
    public const int Keep = 1000;
    public const int MaxPageSize = 200;
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private readonly ModularrDbContext _db;
    private readonly ActivityFeed _feed;
    private readonly List<Activity> _pending = [];

    public ActivityLog(ModularrDbContext db, ActivityFeed feed)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
        _feed = feed;
        db.SavedChanges += (_, _) => PublishSaved();
    }

    // Saved by the caller's next SaveChanges, so an activity never outlives a change that failed.
    public void Add(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        activity.Time = activity.Time == default ? DateTime.UtcNow : activity.Time;
        _db.Activities.Add(activity);
        _pending.Add(activity);
    }

    public async Task RecordAsync(Activity activity, CancellationToken cancellationToken)
    {
        Add(activity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Newest first, older than the before id when given.
    public async Task<IReadOnlyList<Activity>> ListAsync(int? before, int limit, int? seriesId, int? movieId, CancellationToken cancellationToken) =>
        await _db.Activities.AsNoTracking()
            .Where(a => (before == null || a.Id < before) && (seriesId == null || a.SeriesId == seriesId) && (movieId == null || a.MovieId == movieId))
            .OrderByDescending(a => a.Id).Take(Math.Clamp(limit, 1, MaxPageSize))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<int> UnreadCountAsync(CancellationToken cancellationToken) => _db.Activities.CountAsync(a => !a.Read, cancellationToken);

    // Every activity, or the ones up to an id (the newest the client has shown).
    public Task<int> MarkReadAsync(int? upTo, CancellationToken cancellationToken) =>
        _db.Activities.Where(a => !a.Read && (upTo == null || a.Id <= upTo))
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.Read, true), cancellationToken);

    // Keeps the Keep newest activities of the last MaxAge.
    public async Task<int> PruneAsync(DateTime now, CancellationToken cancellationToken)
    {
        int? oldestKept = await _db.Activities.OrderByDescending(a => a.Id).Skip(Keep - 1).Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        DateTime cutoff = now - MaxAge;
        return await _db.Activities.Where(a => a.Time < cutoff || a.Id < oldestKept).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private void PublishSaved()
    {
        foreach (Activity activity in _pending.Where(a => _db.Entry(a).State == EntityState.Unchanged))
        {
            _feed.Publish(new ServerEvent(ServerEvent.Activity, activity));
        }

        // Still Added: the save failed and a later one may keep it; detached: the caller dropped it.
        _pending.RemoveAll(a => _db.Entry(a).State != EntityState.Added);
    }
}
