using System.Collections.Frozen;
using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Languages;
using Modularr.Core.Metadata;

namespace Modularr.Core.Discovery;

// A show of the TVmaze index with what explore sorts it by: TVmaze's follower weight and the premiere date.
public sealed record IndexedShow(DiscoveryItem Item, int Weight, DateOnly? Premiered);

// Keyless show lists: TVmaze for series, AniList for anime (mapped to TVDB through Fribb's anime-lists). Sonarr has no discovery of its own.
public sealed partial class ShowDiscoveryClient(HttpClient httpClient, IOptions<ModularrOptions> options)
{
    public const int ListSize = 40;

    // News, talk and sports fill "airing today" but are not what a library collects.
    private static readonly FrozenSet<string> IgnoredShowTypes = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "News", "Talk Show", "Sports", "Game Show", "Award Show");

    private static readonly string[] Seasons = ["WINTER", "SPRING", "SUMMER", "FALL"];

    // AniList's countryOfOrigin for the languages anime come in.
    private static readonly FrozenDictionary<string, string> AnimeCountries = new Dictionary<string, string>
    {
        [Language.Japanese.Name] = "JP",
        [Language.Korean.Name] = "KR",
        [Language.Chinese.Name] = "CN",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private const string MediaFields = """
        id status seasonYear description genres averageScore bannerImage
        title { romaji english }
        coverImage { extraLarge }
        relations { edges { relationType node { id } } }
        """;

    private const string AnimeQuery = $$"""
        query ($sort: [MediaSort], $season: MediaSeason, $year: Int) {
          Page(perPage: 50) {
            media(type: ANIME, sort: $sort, season: $season, seasonYear: $year, isAdult: false, format_not_in: [MOVIE, MUSIC]) { {{MediaFields}} }
          }
        }
        """;

    // Variables left out are not filters.
    private const string ExploreAnimeQuery = $$"""
        query ($page: Int, $sort: [MediaSort], $genre: String, $year: Int, $from: FuzzyDateInt, $to: FuzzyDateInt, $score: Int,
               $status: MediaStatus, $country: CountryCode) {
          Page(page: $page, perPage: 50) {
            pageInfo { hasNextPage }
            media(type: ANIME, sort: $sort, genre: $genre, seasonYear: $year, startDate_greater: $from, startDate_lesser: $to,
                  averageScore_greater: $score, status: $status, countryOfOrigin: $country, isAdult: false, format_not_in: [MOVIE, MUSIC]) { {{MediaFields}} }
          }
        }
        """;

    private readonly MetadataOptions _options = options.Value.Metadata;

    // shortcut: TVmaze has no popularity ranking; its first index page holds the long-tracked mainstream shows, ordered here by TVmaze's
    // follower weight. Switch to a ranked list if a keyless one appears.
    public async Task<IReadOnlyList<DiscoveryItem>> GetPopularShowsAsync(CancellationToken cancellationToken) =>
        MapShows(await GetAsync<List<TvMazeShow>>(MetadataHttp.Combine(_options.TvMazeUrl, "shows?page=0"), cancellationToken).ConfigureAwait(false));

    // Every show with an episode in the coming days, most followed first.
    public async Task<IReadOnlyList<DiscoveryItem>> GetTrendingShowsAsync(CancellationToken cancellationToken) =>
        MapShows(ShowsOf(await GetAsync<List<TvMazeEpisode>>(MetadataHttp.Combine(_options.TvMazeUrl, "schedule/full"), cancellationToken).ConfigureAwait(false)));

    // Today's US broadcast and streaming schedule.
    public async Task<IReadOnlyList<DiscoveryItem>> GetAiringShowsAsync(CancellationToken cancellationToken)
    {
        List<TvMazeEpisode> broadcast = await GetAsync<List<TvMazeEpisode>>(MetadataHttp.Combine(_options.TvMazeUrl, "schedule?country=US"), cancellationToken).ConfigureAwait(false);
        List<TvMazeEpisode> web = await GetAsync<List<TvMazeEpisode>>(MetadataHttp.Combine(_options.TvMazeUrl, "schedule/web"), cancellationToken).ConfigureAwait(false);
        return MapShows(ShowsOf([.. broadcast, .. web]));
    }

    public Task<IReadOnlyList<DiscoveryItem>> GetTrendingAnimeAsync(IReadOnlyDictionary<int, int> tvdbByAniList, CancellationToken cancellationToken) =>
        GetAnimeAsync(new { sort = "TRENDING_DESC" }, tvdbByAniList, cancellationToken);

    // The current broadcast season (winter = January to March), most popular first.
    public Task<IReadOnlyList<DiscoveryItem>> GetSeasonAnimeAsync(IReadOnlyDictionary<int, int> tvdbByAniList, DateOnly today, CancellationToken cancellationToken) =>
        GetAnimeAsync(new { sort = "POPULARITY_DESC", season = Seasons[(today.Month - 1) / 3], year = today.Year }, tvdbByAniList, cancellationToken);

    // AniList id -> TVDB id.
    public async Task<FrozenDictionary<int, int>> GetAnimeMappingAsync(CancellationToken cancellationToken)
    {
        List<AnimeMapping> mappings = await GetAsync<List<AnimeMapping>>(_options.AnimeMappingUrl, cancellationToken).ConfigureAwait(false);
        return mappings
            .Where(m => m is { AniListId: not null, TvdbId: > 0 })
            .DistinctBy(m => m.AniListId)
            .ToFrozenDictionary(m => m.AniListId!.Value, m => m.TvdbId!.Value);
    }

    // One page of TVmaze's whole show index (by TVmaze id), null past the last one. The overview is left out: the index holds tens
    // of thousands of shows in memory.
    public async Task<IReadOnlyList<IndexedShow>?> GetShowIndexPageAsync(int page, CancellationToken cancellationToken) =>
        await MetadataHttp.GetJsonAsync<List<TvMazeShow>>(httpClient, MetadataHttp.Combine(_options.TvMazeUrl, $"shows?page={page}"), cancellationToken).ConfigureAwait(false)
            is List<TvMazeShow> shows
            ? [.. Collectable(shows).Select(s => new IndexedShow(ToItem(s) with { Overview = null }, s.Weight, s.Premiered))]
            : null;

    // A page of anime matching the filters (sorted by AniList), and whether AniList has more.
    public async Task<(IReadOnlyList<DiscoveryItem> Items, bool HasMore)> ExploreAnimeAsync(
        ExploreQuery query, IReadOnlyDictionary<int, int> tvdbByAniList, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Dictionary<string, object> variables = new()
        {
            ["page"] = query.Page,
            ["sort"] = query.Sort switch
            {
                ExploreSort.Rating => "SCORE_DESC",
                ExploreSort.Date => "START_DATE_DESC",
                ExploreSort.Title => "TITLE_ROMAJI",
                _ => "POPULARITY_DESC",
            },
        };
        void Set(string name, object? value)
        {
            if (value is not null)
            {
                variables[name] = value;
            }
        }

        Set("genre", query.Genre);
        Set("year", query.Year);

        // FuzzyDateInt is yyyymmdd and both bounds are exclusive.
        Set("from", query.YearFrom is int from ? (from * 10000) - 1 : null);
        Set("to", query.YearTo is int to ? (to + 1) * 10000 : null);
        Set("score", query.MinRating is decimal rating ? (int)(rating * 10) - 1 : null);
        Set("status", query.Status switch
        {
            ExploreStatus.Continuing => "RELEASING",
            ExploreStatus.Ended => "FINISHED",
            ExploreStatus.Upcoming => "NOT_YET_RELEASED",
            _ => null,
        });
        Set("country", query.Language is null ? null : AnimeCountries.GetValueOrDefault(query.Language.Name)
            ?? throw new ArgumentException("Anime languages are Japanese, Korean and Chinese."));

        AniListResponse response = await MetadataHttp.PostJsonAsync<AniListResponse>(httpClient, _options.AniListUrl, new { query = ExploreAnimeQuery, variables }, cancellationToken).ConfigureAwait(false)
            ?? throw new MetadataSourceException("AniList answered 404.");
        return (MapAnime(response.Data?.Page?.Media ?? [], tvdbByAniList), response.Data?.Page?.PageInfo?.HasNextPage ?? false);
    }

    // AniList's genres, adult ones aside.
    public async Task<IReadOnlyList<string>> GetAnimeGenresAsync(CancellationToken cancellationToken)
    {
        AniListResponse response = await MetadataHttp.PostJsonAsync<AniListResponse>(httpClient, _options.AniListUrl, new { query = "query { GenreCollection }" }, cancellationToken).ConfigureAwait(false)
            ?? throw new MetadataSourceException("AniList answered 404.");
        return [.. (response.Data?.GenreCollection ?? []).Where(g => !g.Equals("Hentai", StringComparison.OrdinalIgnoreCase))];
    }

    private static IReadOnlyList<DiscoveryItem> MapShows(IEnumerable<TvMazeShow> shows) =>
        [.. Collectable(shows)
            .OrderByDescending(s => s.Weight)
            .ThenByDescending(s => s.Rating?.Average ?? 0)
            .Take(ListSize)
            .Select(ToItem)];

    // A library collects shows TVDB knows, once each.
    private static IEnumerable<TvMazeShow> Collectable(IEnumerable<TvMazeShow> shows) =>
        shows.Where(s => s.Externals?.TheTvdb is not null && !IgnoredShowTypes.Contains(s.Type ?? "")).DistinctBy(s => s.Externals!.TheTvdb);

    private static DiscoveryItem ToItem(TvMazeShow s) => new()
    {
        Kind = s.Genres.Contains("Anime", StringComparer.OrdinalIgnoreCase) ? MediaType.Anime : MediaType.Series,
        TvdbId = s.Externals!.TheTvdb,
        ImdbId = s.Externals.Imdb,
        Title = s.Name,
        Year = s.Premiered?.Year,
        Overview = PlainText(s.Summary),
        Status = (s.Status switch
        {
            "Ended" => SeriesStatus.Ended,
            "In Development" => SeriesStatus.Upcoming,
            _ => SeriesStatus.Continuing,
        }).ToString(),
        Genres = s.Genres,
        Language = s.Language,
        Poster = s.Image?.Original,
        Rating = s.Rating?.Average,
    };

    // A sequel season is often missing from the mapping while its prequel (the same TVDB series) is there.
    private static IReadOnlyList<DiscoveryItem> MapAnime(IEnumerable<AniListMedia> media, IReadOnlyDictionary<int, int> tvdbByAniList) =>
        [.. media
            .Select(m => (Media: m, TvdbId: tvdbByAniList.TryGetValue(m.Id, out int tvdbId) ? tvdbId
                : m.Relations?.Edges.Where(e => e.RelationType is "PREQUEL" or "PARENT")
                    .Select(e => tvdbByAniList.GetValueOrDefault(e.Node.Id)).FirstOrDefault(id => id > 0) ?? 0))
            .Where(m => m.TvdbId > 0)
            .DistinctBy(m => m.TvdbId)
            .Select(m => new DiscoveryItem
            {
                Kind = MediaType.Anime,
                TvdbId = m.TvdbId,
                Title = m.Media.Title.English ?? m.Media.Title.Romaji ?? "",
                Year = m.Media.SeasonYear,
                Overview = PlainText(m.Media.Description),
                Status = (m.Media.Status switch
                {
                    "FINISHED" or "CANCELLED" => SeriesStatus.Ended,
                    "NOT_YET_RELEASED" => SeriesStatus.Upcoming,
                    _ => SeriesStatus.Continuing,
                }).ToString(),
                Genres = ["Anime", .. m.Media.Genres],
                Poster = m.Media.CoverImage?.ExtraLarge,
                Backdrop = m.Media.BannerImage,
                Rating = m.Media.AverageScore / 10m,
            })];

    private async Task<IReadOnlyList<DiscoveryItem>> GetAnimeAsync(object variables, IReadOnlyDictionary<int, int> tvdbByAniList, CancellationToken cancellationToken)
    {
        AniListResponse response = await MetadataHttp.PostJsonAsync<AniListResponse>(httpClient, _options.AniListUrl, new { query = AnimeQuery, variables }, cancellationToken).ConfigureAwait(false)
            ?? throw new MetadataSourceException("AniList answered 404.");
        return [.. MapAnime(response.Data?.Page?.Media ?? [], tvdbByAniList).Take(ListSize)];
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken)
        where T : class =>
        await MetadataHttp.GetJsonAsync<T>(httpClient, url, cancellationToken).ConfigureAwait(false)
            ?? throw new MetadataSourceException($"{url} answered 404.");

    // The country schedule embeds the show as "show", the web schedule as "_embedded.show".
    private static IEnumerable<TvMazeShow> ShowsOf(IEnumerable<TvMazeEpisode> episodes) =>
        episodes.Select(e => e.Show ?? e.Embedded?.Show).OfType<TvMazeShow>();

    private static string? PlainText(string? html) =>
        string.IsNullOrWhiteSpace(html) ? null : WebUtility.HtmlDecode(TagRegex.Replace(html, "")).Trim();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagRegex { get; }

    private sealed record TvMazeShow
    {
        public required string Name { get; init; }
        public string? Type { get; init; }
        public string? Language { get; init; }
        public List<string> Genres { get; init; } = [];
        public string? Status { get; init; }
        public DateOnly? Premiered { get; init; }
        public TvMazeRating? Rating { get; init; }
        public int Weight { get; init; }
        public TvMazeExternals? Externals { get; init; }
        public TvMazeImage? Image { get; init; }
        public string? Summary { get; init; }
    }

    private sealed record TvMazeRating(decimal? Average);

    private sealed record TvMazeExternals([property: JsonPropertyName("thetvdb")] int? TheTvdb, string? Imdb);

    private sealed record TvMazeImage(string? Original);

    private sealed record TvMazeEpisode(TvMazeShow? Show, [property: JsonPropertyName("_embedded")] TvMazeEmbedded? Embedded);

    private sealed record TvMazeEmbedded(TvMazeShow? Show);

    private sealed record AniListResponse(AniListData? Data);

    private sealed record AniListData(AniListPage? Page, List<string>? GenreCollection);

    private sealed record AniListPage(List<AniListMedia> Media, AniListPageInfo? PageInfo);

    private sealed record AniListPageInfo(bool HasNextPage);

    private sealed record AniListMedia
    {
        public int Id { get; init; }
        public string? Status { get; init; }
        public int? SeasonYear { get; init; }
        public required AniListTitle Title { get; init; }
        public string? Description { get; init; }
        public List<string> Genres { get; init; } = [];
        public int? AverageScore { get; init; }
        public AniListCover? CoverImage { get; init; }
        public string? BannerImage { get; init; }
        public AniListRelations? Relations { get; init; }
    }

    private sealed record AniListTitle(string? Romaji, string? English);

    private sealed record AniListCover(string? ExtraLarge);

    private sealed record AniListRelations(List<AniListEdge> Edges);

    private sealed record AniListEdge(string? RelationType, AniListNode Node);

    private sealed record AniListNode(int Id);

    private sealed record AnimeMapping([property: JsonPropertyName("anilist_id")] int? AniListId, [property: JsonPropertyName("tvdb_id")] int? TvdbId);
}
