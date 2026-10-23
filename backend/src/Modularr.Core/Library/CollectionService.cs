using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Modularr.Core.Activities;
using Modularr.Core.Discovery;
using Modularr.Core.Metadata;
using Modularr.Core.Persistence;

namespace Modularr.Core.Library;

// Followed collections and the collections of library movies. Parts reports parts count, null when the source could not be read.
public sealed record CollectionCard(int Id, string Name, string? Poster, string? Backdrop, bool Followed, bool Monitored, int? QualityProfileId, int? Parts, int InLibrary);

public sealed record CollectionPartView(int TmdbId, string Title, int Year, DateOnly? Release, string? Poster, bool Released, int? MovieId, bool HasFile);

public sealed record CollectionDetail(
    int Id, string Name, string? Overview, string? Poster, string? Backdrop, bool Followed, bool Monitored, int? QualityProfileId, IReadOnlyList<CollectionPartView> Parts);

// Ids are signed as in CollectionFollow: a TMDB collection id, or a TMDB keyword id negated. Following never touches library movies.
public sealed class CollectionService(
    ModularrDbContext db,
    MovieMetadataClient movies,
    TmdbClient tmdb,
    LibraryService library,
    ActivityLog activities,
    [FromKeyedServices(DiscoveryService.CacheKey)] IMemoryCache cache)
{
    // Franchises that are not a TMDB collection, offered by the collection search.
    public static readonly IReadOnlyList<CollectionSummary> Franchises = [new(-180547, "Marvel Cinematic Universe", null, null)];

    // Popular sagas Explore offers next to the followed and library ones (ids checked against RadarrAPI and TMDB).
    public static readonly IReadOnlyList<int> Suggested = [-180547, 87096, 1241, 119, 10, 9485, 404609, 87359, 328, 295, 313086];

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    // suggested adds the curated sagas; one that cannot be read (the MCU without a TMDB key) is left out.
    public async Task<IReadOnlyList<CollectionCard>> ListAsync(bool suggested, CancellationToken cancellationToken)
    {
        List<CollectionFollow> followed = await db.Collections.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        List<OwnedMovie> owned = await db.Movies.AsNoTracking().Select(m => new OwnedMovie(m.TmdbId, m.CollectionTmdbId, m.CollectionName)).ToListAsync(cancellationToken).ConfigureAwait(false);
        HashSet<int> ownedIds = [.. owned.Select(m => m.TmdbId)];
        Dictionary<int, string> fromLibrary = owned.Where(m => m.CollectionTmdbId is not null)
            .DistinctBy(m => m.CollectionTmdbId).ToDictionary(m => m.CollectionTmdbId!.Value, m => m.CollectionName ?? "");
        int[] ids = [.. followed.Select(c => c.TmdbId).Union(fromLibrary.Keys).Union(suggested ? Suggested : [])];

        // Few at a time: a cold list fetches one collection per id.
        ConcurrentDictionary<int, CollectionInfo> infos = new();
        await Parallel.ForEachAsync(ids, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (id, ct) =>
        {
            if (await TryFetchAsync(id, ct).ConfigureAwait(false) is CollectionInfo info)
            {
                infos[id] = info;
            }
        }).ConfigureAwait(false);

        return
        [
            .. ids.Where(id => infos.ContainsKey(id) || fromLibrary.ContainsKey(id) || followed.Exists(c => c.TmdbId == id)).Select(id =>
            {
                CollectionFollow? follow = followed.Find(c => c.TmdbId == id);
                CollectionInfo? info = infos.GetValueOrDefault(id);
                return new CollectionCard(
                    id,
                    follow?.Name ?? info?.Name ?? fromLibrary[id],
                    info?.Poster ?? follow?.Poster,
                    info?.Backdrop ?? follow?.Backdrop,
                    follow is not null,
                    follow?.Monitored ?? false,
                    follow?.QualityProfileId,
                    info?.Parts.Count,
                    info is null ? owned.Count(m => m.CollectionTmdbId == id) : info.Parts.Count(p => ownedIds.Contains(p.TmdbId)));
            })
            .OrderByDescending(c => c.Followed)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    public async Task<CollectionDetail> GetAsync(int id, CancellationToken cancellationToken)
    {
        CollectionInfo info = await FetchAsync(id, fresh: false, cancellationToken).ConfigureAwait(false);
        CollectionFollow? follow = await db.Collections.AsNoTracking().FirstOrDefaultAsync(c => c.TmdbId == id, cancellationToken).ConfigureAwait(false);
        int[] tmdbIds = [.. info.Parts.Select(p => p.TmdbId)];
        Dictionary<int, LibraryMovie> inLibrary = await db.Movies.Where(m => tmdbIds.Contains(m.TmdbId))
            .Select(m => new LibraryMovie(m.TmdbId, m.Id, db.MediaFiles.Any(f => f.MovieId == m.Id)))
            .ToDictionaryAsync(m => m.TmdbId, cancellationToken).ConfigureAwait(false);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        return new CollectionDetail(
            id, follow?.Name ?? info.Name, info.Overview, info.Poster ?? follow?.Poster, info.Backdrop ?? follow?.Backdrop,
            follow is not null, follow?.Monitored ?? false, follow?.QualityProfileId,
            [.. info.Parts.Select(p => new CollectionPartView(
                p.TmdbId, p.Title, p.Year, p.Release, p.Poster, p.IsReleased(today),
                inLibrary.GetValueOrDefault(p.TmdbId)?.Id, inLibrary.GetValueOrDefault(p.TmdbId)?.HasFile ?? false))]);
    }

    // Following again updates the existing row.
    public async Task<CollectionDetail> FollowAsync(int id, bool monitored, int? qualityProfileId, bool addMissing, CancellationToken cancellationToken)
    {
        CollectionInfo info = await FetchAsync(id, fresh: false, cancellationToken).ConfigureAwait(false);
        await EnsureProfileAsync(qualityProfileId, cancellationToken).ConfigureAwait(false);
        CollectionFollow? follow = await db.Collections.FirstOrDefaultAsync(c => c.TmdbId == id, cancellationToken).ConfigureAwait(false);
        if (follow is null)
        {
            follow = new CollectionFollow { TmdbId = id, Name = info.Name, Added = DateTime.UtcNow };
            db.Collections.Add(follow);
        }

        follow.Name = info.Name;
        follow.Poster = info.Poster;
        follow.Backdrop = info.Backdrop;
        follow.Monitored = monitored;
        follow.QualityProfileId = qualityProfileId ?? follow.QualityProfileId;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (addMissing)
        {
            await AddMissingAsync(id, includeUpcoming: false, fresh: false, cancellationToken).ConfigureAwait(false);
        }

        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CollectionDetail> UpdateAsync(int id, bool? monitored, int? qualityProfileId, CancellationToken cancellationToken)
    {
        CollectionFollow follow = await db.Collections.FirstOrDefaultAsync(c => c.TmdbId == id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Collection {id} is not followed.");
        await EnsureProfileAsync(qualityProfileId, cancellationToken).ConfigureAwait(false);
        follow.Monitored = monitored ?? follow.Monitored;
        follow.QualityProfileId = qualityProfileId ?? follow.QualityProfileId;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnfollowAsync(int id, CancellationToken cancellationToken)
    {
        if (await db.Collections.Where(c => c.TmdbId == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            throw new KeyNotFoundException($"Collection {id} is not followed.");
        }
    }

    // Adds the parts not in the library with the followed profile (else the movie default): released ones searched now, upcoming
    // ones (includeUpcoming) monitored for the scheduled search. A part that cannot be added is skipped. Returns the added movies.
    public async Task<IReadOnlyList<Movie>> AddMissingAsync(int id, bool includeUpcoming, bool fresh, CancellationToken cancellationToken)
    {
        CollectionInfo info = await FetchAsync(id, fresh, cancellationToken).ConfigureAwait(false);
        int[] tmdbIds = [.. info.Parts.Select(p => p.TmdbId)];
        HashSet<int> owned = [.. await db.Movies.Where(m => tmdbIds.Contains(m.TmdbId)).Select(m => m.TmdbId).ToListAsync(cancellationToken).ConfigureAwait(false)];
        int? profileId = await db.Collections.Where(c => c.TmdbId == id).Select(c => c.QualityProfileId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        List<Movie> added = [];
        foreach (CollectionPart part in Missing(info.Parts, owned, today, includeUpcoming))
        {
            try
            {
                added.Add(await library.AddMovieAsync(part.TmdbId, profileId, version: null, search: part.IsReleased(today), cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is MetadataNotFoundException or MetadataSourceException or ArgumentException)
            {
            }
        }

        return added;
    }

    // The monitored followed collections pick up new parts; run after the daily metadata refresh. A source that fails is retried next round.
    public async Task<int> RefreshFollowedAsync(CancellationToken cancellationToken)
    {
        int added = 0;
        foreach (CollectionFollow follow in await db.Collections.AsNoTracking().Where(c => c.Monitored).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                IReadOnlyList<Movie> movies = await AddMissingAsync(follow.TmdbId, includeUpcoming: true, fresh: true, cancellationToken).ConfigureAwait(false);
                added += movies.Count;
                if (movies.Count > 0)
                {
                    await activities.RecordAsync(
                        new Activity { Kind = ActivityKind.Added, Level = ActivityLevel.Info, Title = follow.Name, Message = $"{movies.Count} new part(s) from the collection" },
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is MetadataNotFoundException or MetadataSourceException or InvalidOperationException)
            {
            }
        }

        return added;
    }

    // Collections by name: TMDB's search (needs a key) plus the matching built-in franchises.
    public async Task<IReadOnlyList<CollectionSummary>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return [];
        }

        IEnumerable<CollectionSummary> franchises = Franchises.Where(f =>
            f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || (f.Id == -180547 && query.Contains("mcu", StringComparison.OrdinalIgnoreCase)));
        IReadOnlyList<CollectionSummary> found = tmdb.Enabled
            ? await cache.GetOrCreateAsync($"collections:search:{query.ToLowerInvariant()}", entry =>
            {
                entry.Size = 1;
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return tmdb.SearchCollectionsAsync(query, cancellationToken);
            }).ConfigureAwait(false) ?? []
            : [];
        return [.. franchises.Concat(found)];
    }

    // What add-missing adds: parts not in the library, released ones only unless includeUpcoming.
    public static IReadOnlyList<CollectionPart> Missing(IEnumerable<CollectionPart> parts, IReadOnlySet<int> library, DateOnly today, bool includeUpcoming) =>
        [.. parts.Where(p => !library.Contains(p.TmdbId) && (includeUpcoming || p.IsReleased(today)))];

    private async Task<CollectionInfo> FetchAsync(int id, bool fresh, CancellationToken cancellationToken)
    {
        string key = $"collection:{id}";
        if (fresh)
        {
            cache.Remove(key);
        }

        return await cache.GetOrCreateAsync(key, entry =>
        {
            entry.Size = 1;
            entry.AbsoluteExpirationRelativeToNow = Lifetime;
            return id switch
            {
                > 0 => movies.GetCollectionAsync(id, cancellationToken),
                < 0 => KeywordFranchiseAsync(-id, cancellationToken),
                _ => throw new ArgumentException("Collection id 0 does not exist.", nameof(id)),
            };
        }).ConfigureAwait(false) ?? throw new MetadataNotFoundException(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // TMDB keyword names are lower case ("marvel cinematic universe (mcu)"); a built-in franchise keeps its own name.
    private async Task<CollectionInfo> KeywordFranchiseAsync(int keywordId, CancellationToken cancellationToken)
    {
        if (!tmdb.Enabled)
        {
            throw new ArgumentException("Franchises need a TMDB key (Settings, Metadata).", nameof(keywordId));
        }

        CollectionInfo info = await tmdb.GetKeywordFranchiseAsync(keywordId, cancellationToken).ConfigureAwait(false);
        return Franchises.FirstOrDefault(f => f.Id == info.Id) is CollectionSummary known ? info with { Name = known.Name } : info;
    }

    private async Task<CollectionInfo?> TryFetchAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await FetchAsync(id, fresh: false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task EnsureProfileAsync(int? qualityProfileId, CancellationToken cancellationToken)
    {
        if (qualityProfileId is int id && !await db.QualityProfiles.AnyAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException($"Quality profile {id} does not exist.", nameof(qualityProfileId));
        }
    }

    private sealed record OwnedMovie(int TmdbId, int? CollectionTmdbId, string? CollectionName);

    private sealed record LibraryMovie(int TmdbId, int Id, bool HasFile);
}
