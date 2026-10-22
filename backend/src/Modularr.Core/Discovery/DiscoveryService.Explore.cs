using System.Collections.Frozen;
using Modularr.Core.Decisions;
using Modularr.Core.Metadata;

namespace Modularr.Core.Discovery;

// /api/explore: filtered, sorted and paged catalogues. Movies come from TMDB discover when a key is set (otherwise the keyless lists,
// filtered here and flagged limited), series from the TVmaze index, anime from AniList.
public sealed partial class DiscoveryService
{
    public const int ExplorePageSize = 40;
    private static readonly TimeSpan ExploreLifetime = TimeSpan.FromHours(1);

    public async Task<ExplorePage> ExploreAsync(MediaType kind, ExploreQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.EnsureValid(kind);
        bool limited = kind == MediaType.Movie && !tmdb.Enabled;
        try
        {
            ExplorePage page = kind switch
            {
                MediaType.Movie when limited => Page(await KeylessMoviesAsync(cancellationToken).ConfigureAwait(false), query, limited: true),
                MediaType.Movie => await TmdbPageAsync(query, cancellationToken).ConfigureAwait(false),
                MediaType.Anime => await AnimePageAsync(query, cancellationToken).ConfigureAwait(false),
                _ => SeriesPage(query),
            };
            return page with { Items = await WithLibraryAsync(page.Items, cancellationToken).ConfigureAwait(false) };
        }
        catch (MetadataSourceException ex)
        {
            return new ExplorePage([], query.Page, HasMore: false, limited, Error: ex.Message);
        }
    }

    // The genres a kind can be filtered by, sorted.
    public async Task<IReadOnlyList<string>> ExploreGenresAsync(MediaType kind, CancellationToken cancellationToken)
    {
        IEnumerable<string> genres = kind switch
        {
            MediaType.Movie when tmdb.Enabled => (await TmdbGenresAsync(cancellationToken).ConfigureAwait(false)).Values,
            MediaType.Movie => (await KeylessMoviesAsync(cancellationToken).ConfigureAwait(false)).SelectMany(m => m.Item.Genres),
            MediaType.Anime => await CachedAsync("explore:anime-genres", ListLifetime, shows.GetAnimeGenresAsync, cancellationToken).ConfigureAwait(false),
            _ => showIndex.Current().Shows.Where(s => s.Item.Kind == MediaType.Series).SelectMany(s => s.Item.Genres),
        };
        return [.. genres.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
    }

    // Filters and sorts a list held here. Popularity is the source's own scale; Date, when known, is finer than the year.
    public static ExplorePage Page(IEnumerable<(DiscoveryItem Item, double Popularity, DateOnly? Date)> items, ExploreQuery query, bool limited = false, bool loading = false)
    {
        ArgumentNullException.ThrowIfNull(query);
        IEnumerable<(DiscoveryItem Item, double Popularity, DateOnly? Date)> matching = items.Where(i => query.Matches(i.Item, i.Date));
        IEnumerable<(DiscoveryItem Item, double Popularity, DateOnly? Date)> sorted = query.Sort switch
        {
            ExploreSort.Rating => matching.OrderByDescending(i => i.Item.Rating ?? -1).ThenByDescending(i => i.Popularity),
            ExploreSort.Date => matching.OrderByDescending(i => i.Date ?? (i.Item.Year is int year ? new DateOnly(year, 1, 1) : DateOnly.MinValue)),
            ExploreSort.Title => matching.OrderBy(i => i.Item.Title, StringComparer.OrdinalIgnoreCase),
            _ => matching.OrderByDescending(i => i.Popularity),
        };
        DiscoveryItem[] window = [.. sorted.Skip((query.Page - 1) * ExplorePageSize).Take(ExplorePageSize + 1).Select(i => i.Item)];
        return new ExplorePage([.. window.Take(ExplorePageSize)], query.Page, window.Length > ExplorePageSize, limited, loading);
    }

    // Anime appear under their own kind, not as series.
    private ExplorePage SeriesPage(ExploreQuery query)
    {
        (IReadOnlyList<IndexedShow> index, bool loading) = showIndex.Current();
        ExplorePage page = Page(index.Where(s => s.Item.Kind == MediaType.Series).Select(s => (s.Item, (double)s.Weight, s.Premiered)), query, loading: loading);
        return index.Count == 0 && !loading && showIndex.LastError is string error ? page with { Error = error } : page;
    }

    private async Task<ExplorePage> TmdbPageAsync(ExploreQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<int, string> genres = await TmdbGenresAsync(cancellationToken).ConfigureAwait(false);
        (IReadOnlyList<DiscoveryItem> items, bool hasMore) = await CachedAsync(
            $"explore:movies:{query}", ExploreLifetime, ct => tmdb.DiscoverAsync(query, genres, DateOnly.FromDateTime(DateTime.UtcNow), ct), cancellationToken).ConfigureAwait(false);
        return new ExplorePage(items, query.Page, hasMore);
    }

    private Task<IReadOnlyDictionary<int, string>> TmdbGenresAsync(CancellationToken cancellationToken) =>
        CachedAsync("explore:tmdb-genres", ListLifetime, tmdb.GetGenresAsync, cancellationToken);

    private async Task<ExplorePage> AnimePageAsync(ExploreQuery query, CancellationToken cancellationToken)
    {
        FrozenDictionary<int, int> mapping = await CachedAsync("discover:anime-mapping", ListLifetime, shows.GetAnimeMappingAsync, cancellationToken).ConfigureAwait(false);
        (IReadOnlyList<DiscoveryItem> items, bool hasMore) = await CachedAsync(
            $"explore:anime:{query}", ExploreLifetime, ct => shows.ExploreAnimeAsync(query, mapping, ct), cancellationToken).ConfigureAwait(false);
        return new ExplorePage(items, query.Page, hasMore);
    }

    // RadarrAPI's lists merged, with each movie's earliest release.
    private Task<IReadOnlyList<(DiscoveryItem Item, double Popularity, DateOnly? Date)>> KeylessMoviesAsync(CancellationToken cancellationToken) =>
        CachedAsync<IReadOnlyList<(DiscoveryItem, double, DateOnly?)>>("explore:movies:keyless", ListLifetime, async ct =>
        {
            IReadOnlyList<MovieInfo>[] lists = await Task.WhenAll(
                movies.GetListAsync("tmdb/popular", ct), movies.GetListAsync("tmdb/trending", ct), movies.GetListAsync("imdb/popular", ct)).ConfigureAwait(false);
            return
            [
                .. lists.SelectMany(l => l).DistinctBy(m => m.TmdbId).Select(m => (
                    DiscoveryItem.From(m),
                    (double)m.Popularity,
                    new[] { m.InCinemas, m.DigitalRelease, m.PhysicalRelease }.Min() is DateTime released ? DateOnly.FromDateTime(released) : (DateOnly?)null)),
            ];
        }, cancellationToken);
}
