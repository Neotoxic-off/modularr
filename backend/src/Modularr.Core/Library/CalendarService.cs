using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Modularr.Core.Persistence;

namespace Modularr.Core.Library;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(EpisodeEvent), "episode")]
[JsonDerivedType(typeof(MovieEvent), "movie")]
public abstract record CalendarEvent(DateOnly Date, string Title, string? Poster, bool HasFile, bool Monitored);

// Kind is series or anime.
public sealed record EpisodeEvent(
    DateOnly Date, int SeriesId, string Title, int Season, int Episode, string? EpisodeTitle, string? Poster, string Kind, bool HasFile, bool Monitored)
    : CalendarEvent(Date, Title, Poster, HasFile, Monitored);

// Release is cinema, digital or physical: a movie has one event per known release day.
public sealed record MovieEvent(DateOnly Date, int MovieId, string Title, string Release, string? Poster, bool HasFile, bool Monitored)
    : CalendarEvent(Date, Title, Poster, HasFile, Monitored);

// Episode air days and movie release days, one query each.
public sealed class CalendarService(ModularrDbContext db)
{
    public const int MaxDays = 62;
    public const int UpcomingCount = 10;
    public const int UpcomingMonths = 6;

    // Start and end are both included.
    public Task<IReadOnlyList<CalendarEvent>> GetAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        if (end < start || end.DayNumber - start.DayNumber >= MaxDays)
        {
            throw new ArgumentException($"The range must run forward and span at most {MaxDays} days.", nameof(end));
        }

        return EventsAsync(start, end, take: null, cancellationToken);
    }

    // The next events from the given day on, for an empty calendar range.
    public Task<IReadOnlyList<CalendarEvent>> UpcomingAsync(DateOnly from, CancellationToken cancellationToken) =>
        EventsAsync(from, from.AddMonths(UpcomingMonths), UpcomingCount, cancellationToken);

    private async Task<IReadOnlyList<CalendarEvent>> EventsAsync(DateOnly start, DateOnly end, int? take, CancellationToken cancellationToken)
    {
        IQueryable<EpisodeEvent> episodeQuery = db.Episodes.AsNoTracking()
            .Where(e => e.AirDate >= start && e.AirDate <= end)
            .OrderBy(e => e.AirDate).ThenBy(e => e.Series.SortTitle).ThenBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber)
            .Select(e => new EpisodeEvent(
                e.AirDate!.Value, e.SeriesId, e.Series.Title, e.SeasonNumber, e.EpisodeNumber, e.Title, e.Series.Poster,
                e.Series.SeriesType == SeriesType.Anime ? "anime" : "series", e.MediaFileId != null, e.Monitored && e.Series.Monitored));
        List<EpisodeEvent> episodes = await (take is int count ? episodeQuery.Take(count) : episodeQuery)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<CalendarEvent> releases = (await db.Movies.AsNoTracking()
            .Where(m => (m.InCinemas >= start && m.InCinemas <= end) || (m.DigitalRelease >= start && m.DigitalRelease <= end)
                || (m.PhysicalRelease >= start && m.PhysicalRelease <= end))
            .Select(m => new { m.Id, m.Title, m.Poster, m.Monitored, m.InCinemas, m.DigitalRelease, m.PhysicalRelease, HasFile = db.MediaFiles.Any(f => f.MovieId == m.Id) })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .SelectMany(m => new[] { (Day: m.InCinemas, Release: "cinema"), (Day: m.DigitalRelease, Release: "digital"), (Day: m.PhysicalRelease, Release: "physical") }
                .Where(r => r.Day >= start && r.Day <= end)
                .Select(r => new MovieEvent(r.Day!.Value, m.Id, m.Title, r.Release, m.Poster, m.HasFile, m.Monitored)));

        IEnumerable<CalendarEvent> sorted = episodes.Concat(releases).OrderBy(e => e.Date).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase);
        return [.. take is int limit ? sorted.Take(limit) : sorted];
    }
}
