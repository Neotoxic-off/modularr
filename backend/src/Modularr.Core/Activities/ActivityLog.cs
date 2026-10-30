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

    // Newest first, older than the before id when given, marked read or not for this user.
    public async Task<IReadOnlyList<Activity>> ListAsync(int userId, int? before, int limit, int? seriesId, int? movieId, CancellationToken cancellationToken)
    {
        int readUpTo = await ReadUpToAsync(userId, cancellationToken).ConfigureAwait(false);
        List<Activity> activities = await _db.Activities.AsNoTracking()
            .Where(a => (before == null || a.Id < before) && (seriesId == null || a.SeriesId == seriesId) && (movieId == null || a.MovieId == movieId))
            .OrderByDescending(a => a.Id).Take(Math.Clamp(limit, 1, MaxPageSize))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        activities.ForEach(a => a.Read = a.Id <= readUpTo);
        return activities;
    }

    public async Task<int> UnreadCountAsync(int userId, CancellationToken cancellationToken)
    {
        int readUpTo = await ReadUpToAsync(userId, cancellationToken).ConfigureAwait(false);
        return await _db.Activities.CountAsync(a => a.Id > readUpTo, cancellationToken).ConfigureAwait(false);
    }

    // Every activity, or the ones up to an id (the newest the client has shown), for this user only. The mark never moves back.
    public async Task MarkReadAsync(int userId, int? upTo, CancellationToken cancellationToken)
    {
        // Never past the newest existing id: a mark set ahead would hide every future activity from this user.
        int existing = await _db.Activities.MaxAsync(a => (int?)a.Id, cancellationToken).ConfigureAwait(false) ?? 0;
        int newest = Math.Min(upTo ?? existing, existing);
        await _db.Users.Where(u => u.Id == userId && u.ReadActivityUpTo < newest)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.ReadActivityUpTo, newest), cancellationToken).ConfigureAwait(false);
    }

    private Task<int> ReadUpToAsync(int userId, CancellationToken cancellationToken) =>
        _db.Users.Where(u => u.Id == userId).Select(u => u.ReadActivityUpTo).FirstOrDefaultAsync(cancellationToken);

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
