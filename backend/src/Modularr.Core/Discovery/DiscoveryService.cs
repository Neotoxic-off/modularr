using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Modularr.Core.Decisions;
using Modularr.Core.Metadata;
using Modularr.Core.Persistence;

namespace Modularr.Core.Discovery;

public enum MovieList
{
    Popular,
    Trending,
    Upcoming,
}

public enum SeriesList
{
    Popular,
    Trending,
    Airing,
}

public enum AnimeList
{
    Trending,
    Season,
}

// Discover lists and the add-box lookup. Source answers are cached in memory; library membership is applied per request, so it is always current.
public sealed class DiscoveryService(
    MovieMetadataClient movies, SeriesMetadataClient series, ShowDiscoveryClient shows, IMemoryCache cache, ModularrDbContext db)
{
    private static readonly TimeSpan ListLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan LookupLifetime = TimeSpan.FromMinutes(5);

    public Task<DiscoveryResult> DiscoverMoviesAsync(MovieList list, CancellationToken cancellationToken) =>
        DiscoverAsync($"discover:movies:{list}", list switch
        {
            MovieList.Popular => async ct => FromMovies(await movies.GetListAsync("tmdb/popular", ct).ConfigureAwait(false)),
            MovieList.Trending => async ct => FromMovies(await movies.GetListAsync("tmdb/trending", ct).ConfigureAwait(false)),
            _ => UpcomingMoviesAsync,
        }, cancellationToken);

    public Task<DiscoveryResult> DiscoverSeriesAsync(SeriesList list, CancellationToken cancellationToken) =>
        DiscoverAsync($"discover:series:{list}", list switch
        {
            SeriesList.Popular => shows.GetPopularShowsAsync,
            SeriesList.Trending => shows.GetTrendingShowsAsync,
            _ => shows.GetAiringShowsAsync,
        }, cancellationToken);

    public Task<DiscoveryResult> DiscoverAnimeAsync(AnimeList list, CancellationToken cancellationToken) =>
        DiscoverAsync($"discover:anime:{list}", async ct =>
        {
            FrozenDictionary<int, int> mapping = await CachedAsync("discover:anime-mapping", ListLifetime, shows.GetAnimeMappingAsync, ct).ConfigureAwait(false);
            return list == AnimeList.Trending
                ? await shows.GetTrendingAnimeAsync(mapping, ct).ConfigureAwait(false)
                : await shows.GetSeasonAnimeAsync(mapping, DateOnly.FromDateTime(DateTime.UtcNow), ct).ConfigureAwait(false);
        }, cancellationToken);

    // Movies and/or series matching what the user typed, best match first. When both are asked for, one source failing still answers with the other.
    public async Task<IReadOnlyList<DiscoveryItem>> LookupAsync(string term, bool includeMovies, bool includeSeries, CancellationToken cancellationToken)
    {
        LookupQuery query = LookupQuery.Parse(term);
        includeMovies &= query.IdSource is null or "tmdb" or "imdb";
        includeSeries &= query.IdSource is null or "tvdb" or "imdb";

        if (query.IsEmpty || !(includeMovies || includeSeries))
        {
            return [];
        }

        IReadOnlyList<DiscoveryItem> items = await CachedAsync($"lookup:{includeMovies}:{includeSeries}:{query.SearchTerm}:{query.Year}", LookupLifetime, async ct =>
        {
            List<Task<IReadOnlyList<DiscoveryItem>>> searches = [];
            if (includeMovies)
            {
                searches.Add(SearchMoviesAsync(query.SearchTerm, ct));
            }

            if (includeSeries)
            {
                searches.Add(SearchSeriesAsync(query.SearchTerm, ct));
            }

            try
            {
                await Task.WhenAll(searches).ConfigureAwait(false);
            }
            catch (MetadataSourceException) when (searches.Any(s => s.IsCompletedSuccessfully))
            {
            }

            return query.Rank(searches.Where(s => s.IsCompletedSuccessfully).Select(s => s.Result));
        }, cancellationToken).ConfigureAwait(false);

        return await WithLibraryAsync(items, cancellationToken).ConfigureAwait(false);
    }

    // Not out anywhere yet (cinema, digital or physical), soonest first. RadarrAPI has no upcoming list, so this draws on the lists it has.
    public static IReadOnlyList<DiscoveryItem> Upcoming(IEnumerable<MovieInfo> candidates, DateTime now) =>
        [.. candidates
            .DistinctBy(m => m.TmdbId)
            .Select(m => (Movie: m, Release: new[] { m.InCinemas, m.DigitalRelease, m.PhysicalRelease }.Min()))
            .Where(m => m.Release > now)
            .OrderBy(m => m.Release)
            .Take(ShowDiscoveryClient.ListSize)
            .Select(m => DiscoveryItem.From(m.Movie))];

    private async Task<IReadOnlyList<DiscoveryItem>> UpcomingMoviesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MovieInfo>[] lists = await Task.WhenAll(
            movies.GetListAsync("tmdb/popular", cancellationToken),
            movies.GetListAsync("tmdb/trending", cancellationToken),
            movies.GetListAsync("imdb/popular", cancellationToken)).ConfigureAwait(false);
        return Upcoming(lists.SelectMany(l => l), DateTime.UtcNow);
    }

    private async Task<IReadOnlyList<DiscoveryItem>> SearchMoviesAsync(string term, CancellationToken cancellationToken) =>
        FromMovies(await movies.SearchMoviesAsync(term, cancellationToken).ConfigureAwait(false));

    private async Task<IReadOnlyList<DiscoveryItem>> SearchSeriesAsync(string term, CancellationToken cancellationToken) =>
        [.. (await series.SearchSeriesAsync(term, cancellationToken: cancellationToken).ConfigureAwait(false)).Select(DiscoveryItem.From)];

    private static IReadOnlyList<DiscoveryItem> FromMovies(IReadOnlyList<MovieInfo> list) => [.. list.Select(DiscoveryItem.From)];

    private async Task<DiscoveryResult> DiscoverAsync(string key, Func<CancellationToken, Task<IReadOnlyList<DiscoveryItem>>> fetch, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<DiscoveryItem> items = await CachedAsync(key, ListLifetime, fetch, cancellationToken).ConfigureAwait(false);
            return new DiscoveryResult(await WithLibraryAsync(items, cancellationToken).ConfigureAwait(false));
        }
        catch (MetadataSourceException ex)
        {
            return new DiscoveryResult([], ex.Message);
        }
    }

    // A failed fetch is not cached. Each entry counts 1 against the cache's SizeLimit, so distinct lookup terms cannot fill memory.
    private async Task<T> CachedAsync<T>(string key, TimeSpan lifetime, Func<CancellationToken, Task<T>> fetch, CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = lifetime;
            entry.Size = 1;
            return fetch(cancellationToken);
        }).ConfigureAwait(false))!;

    private async Task<IReadOnlyList<DiscoveryItem>> WithLibraryAsync(IReadOnlyList<DiscoveryItem> items, CancellationToken cancellationToken)
    {
        int[] tmdbIds = [.. items.Select(i => i.TmdbId).OfType<int>()];
        int[] tvdbIds = [.. items.Select(i => i.TvdbId).OfType<int>()];
        Dictionary<int, int> movieIds = await db.Movies.Where(m => tmdbIds.Contains(m.TmdbId)).ToDictionaryAsync(m => m.TmdbId, m => m.Id, cancellationToken).ConfigureAwait(false);
        Dictionary<int, int> seriesIds = await db.Series.Where(s => tvdbIds.Contains(s.TvdbId)).ToDictionaryAsync(s => s.TvdbId, s => s.Id, cancellationToken).ConfigureAwait(false);

        return [.. items.Select(i => i with { LibraryId = i.Kind == MediaType.Movie ? Find(movieIds, i.TmdbId) : Find(seriesIds, i.TvdbId) })];
    }

    private static int? Find(Dictionary<int, int> ids, int? key) => key is int k && ids.TryGetValue(k, out int id) ? id : null;
}
