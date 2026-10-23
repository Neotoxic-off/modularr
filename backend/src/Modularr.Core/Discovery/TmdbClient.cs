using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Languages;
using Modularr.Core.Metadata;

namespace Modularr.Core.Discovery;

// TMDB's discover API, used once the admin sets a key. The hosts are fixed, the key is the user's own.
public sealed class TmdbClient(HttpClient httpClient, IOptionsMonitor<ModularrOptions> options)
{
    private const string ApiUrl = "https://api.themoviedb.org/3/";
    private const string ImageUrl = "https://image.tmdb.org/t/p/";

    // Rating sorts ignore titles too few people rated.
    private const int MinVotesForRating = 200;

    public bool Enabled => options.CurrentValue.Metadata.EffectiveTmdbApiKey is not null;

    // Null when TMDB accepts the effective key, else why not.
    public async Task<string?> TestAsync(CancellationToken cancellationToken)
    {
        try
        {
            await GetGenresAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is MetadataSourceException or InvalidOperationException)
        {
            return ex.Message;
        }
    }

    // TMDB genre id -> name.
    public async Task<IReadOnlyDictionary<int, string>> GetGenresAsync(CancellationToken cancellationToken) =>
        (await GetAsync<GenreList>("genre/movie/list?language=en-US", cancellationToken).ConfigureAwait(false)).Genres.ToDictionary(g => g.Id, g => g.Name);

    public async Task<(IReadOnlyList<DiscoveryItem> Items, bool HasMore)> DiscoverAsync(
        ExploreQuery query, IReadOnlyDictionary<int, string> genres, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(genres);
        List<string> parameters =
        [
            "include_adult=false",
            "include_video=false",
            "language=en-US",
            Parameter("page", query.Page),
            "sort_by=" + query.Sort switch
            {
                ExploreSort.Rating => "vote_average.desc",
                ExploreSort.Date => "primary_release_date.desc",
                ExploreSort.Title => "title.asc",
                _ => "popularity.desc",
            },
        ];
        if (query.Sort == ExploreSort.Rating)
        {
            parameters.Add(Parameter("vote_count.gte", MinVotesForRating));
        }

        if (query.Genre is string genre)
        {
            int id = genres.FirstOrDefault(g => g.Value.Equals(genre, StringComparison.OrdinalIgnoreCase)).Key;
            parameters.Add(id > 0 ? Parameter("with_genres", id) : throw new ArgumentException($"Unknown movie genre '{genre}'."));
        }

        // The year window and the status both bound the release date; the tighter bound wins.
        DateOnly? from = Latest(query.FirstYear is int first ? new DateOnly(first, 1, 1) : null, query.Status == ExploreStatus.Upcoming ? today.AddDays(1) : null);
        DateOnly? to = Earliest(query.LastYear is int last ? new DateOnly(last, 12, 31) : null, query.Status == ExploreStatus.Released ? today : null);
        if (from is DateOnly gte)
        {
            parameters.Add(Parameter("primary_release_date.gte", gte.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        if (to is DateOnly lte)
        {
            parameters.Add(Parameter("primary_release_date.lte", lte.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        if (query.MinRating is decimal rating)
        {
            parameters.Add(Parameter("vote_average.gte", rating.ToString(CultureInfo.InvariantCulture)));
        }

        if (query.Language is Language language)
        {
            parameters.Add(IsoLanguages.Get(language) is IsoLanguage iso
                ? Parameter("with_original_language", iso.TwoLetterCode)
                : throw new ArgumentException($"TMDB has no code for {language}."));
        }

        DiscoverPage page = await GetAsync<DiscoverPage>("discover/movie?" + string.Join('&', parameters), cancellationToken).ConfigureAwait(false);
        return ([.. page.Results.Select(m => ToItem(m, genres))], page.Page < Math.Min(page.TotalPages, ExploreQuery.MaxPage));
    }

    // TMDB collections matching a name.
    public async Task<IReadOnlyList<CollectionSummary>> SearchCollectionsAsync(string query, CancellationToken cancellationToken)
    {
        CollectionPage page = await GetAsync<CollectionPage>($"search/collection?language=en-US&include_adult=false&{Parameter("query", query)}", cancellationToken).ConfigureAwait(false);
        return [.. page.Results.Select(c => new CollectionSummary(c.Id, c.Name, Image("w500", c.PosterPath), Image("w1280", c.BackdropPath)))];
    }

    // A franchise that is not a TMDB collection (the MCU): every movie tagged with the keyword. The id is returned negated, as stored.
    // shortcut: the first MaxKeywordPages pages (400 movies) only, page further if a franchise ever outgrows that.
    public async Task<CollectionInfo> GetKeywordFranchiseAsync(int keywordId, CancellationToken cancellationToken)
    {
        const int MaxKeywordPages = 20;
        TmdbKeyword keyword = await GetAsync<TmdbKeyword>($"keyword/{keywordId.ToString(CultureInfo.InvariantCulture)}?language=en-US", cancellationToken).ConfigureAwait(false);
        List<TmdbMovie> movies = [];
        for (int page = 1; page <= MaxKeywordPages; page++)
        {
            DiscoverPage result = await GetAsync<DiscoverPage>(
                $"discover/movie?include_adult=false&include_video=false&language=en-US&sort_by=primary_release_date.asc&{Parameter("with_keywords", keywordId)}&{Parameter("page", page)}",
                cancellationToken).ConfigureAwait(false);
            movies.AddRange(result.Results);
            if (result.Page >= result.TotalPages)
            {
                break;
            }
        }

        IReadOnlyList<CollectionPart> parts = CollectionInfo.InReleaseOrder(movies.Select(m =>
        {
            DateOnly? released = DateOnly.TryParseExact(m.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day) ? day : null;
            return new CollectionPart(m.Id, m.Title, released?.Year ?? 0, released, Image("w500", m.PosterPath), Image("w1280", m.BackdropPath));
        }));
        // Named after the keyword, pictured by its latest released part.
        CollectionPart? cover = parts.LastOrDefault(p => p.Poster is not null && p.IsReleased(DateOnly.FromDateTime(DateTime.UtcNow)));
        return new CollectionInfo
        {
            Id = -keywordId,
            Name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(keyword.Name),
            Poster = cover?.Poster,
            Backdrop = cover?.Backdrop,
            Parts = parts,
        };
    }

    private static string? Image(string size, string? path) => path is null ? null : ImageUrl + size + path;

    private static DiscoveryItem ToItem(TmdbMovie movie, IReadOnlyDictionary<int, string> genres) => new()
    {
        Kind = MediaType.Movie,
        TmdbId = movie.Id,
        Title = movie.Title,
        Year = DateOnly.TryParseExact(movie.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly released) ? released.Year : null,
        Overview = string.IsNullOrWhiteSpace(movie.Overview) ? null : movie.Overview,
        Genres = [.. movie.GenreIds.Select(genres.GetValueOrDefault).OfType<string>()],
        Language = movie.OriginalLanguage is string code ? IsoLanguages.Find(code)?.Language is { Id: > 0 } known ? known.Name : null : null,
        Poster = movie.PosterPath is string poster ? ImageUrl + "w500" + poster : null,
        Backdrop = movie.BackdropPath is string backdrop ? ImageUrl + "w1280" + backdrop : null,
        Rating = movie.VoteCount > 0 ? Math.Round(movie.VoteAverage, 1) : null,
    };

    // A v4 read access token (a JWT) goes in the Authorization header, a v3 key in the query. Errors never repeat the URL, which
    // would show a v3 key.
    private async Task<T> GetAsync<T>(string relative, CancellationToken cancellationToken)
        where T : class
    {
        string key = options.CurrentValue.Metadata.EffectiveTmdbApiKey ?? throw new InvalidOperationException("No TMDB key is set.");
        bool bearer = key.Contains('.', StringComparison.Ordinal);
        string url = ApiUrl + relative + (bearer ? "" : "&api_key=" + Uri.EscapeDataString(key));
        try
        {
            return await MetadataHttp.GetJsonAsync<T>(httpClient, url, cancellationToken, bearer ? new AuthenticationHeaderValue("Bearer", key) : null).ConfigureAwait(false)
                ?? throw new MetadataSourceException("TMDB answered 404.");
        }
        catch (MetadataSourceException ex)
        {
            throw new MetadataSourceException($"TMDB request failed: {(ex.InnerException as HttpRequestException)?.StatusCode?.ToString() ?? ex.InnerException?.GetType().Name ?? "no answer"}.");
        }
    }

    private static string Parameter(string name, object value) =>
        $"{name}={Uri.EscapeDataString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")}";

    private static DateOnly? Latest(DateOnly? a, DateOnly? b) => a is null ? b : b is null ? a : a > b ? a : b;

    private static DateOnly? Earliest(DateOnly? a, DateOnly? b) => a is null ? b : b is null ? a : a < b ? a : b;

    private sealed record GenreList(List<TmdbGenre> Genres);

    private sealed record TmdbGenre(int Id, string Name);

    private sealed record DiscoverPage(int Page, [property: JsonPropertyName("total_pages")] int TotalPages, List<TmdbMovie> Results);

    private sealed record TmdbKeyword(int Id, string Name);

    private sealed record CollectionPage(List<TmdbCollection> Results);

    private sealed record TmdbCollection
    {
        public int Id { get; init; }
        public required string Name { get; init; }
        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; init; }
        [JsonPropertyName("backdrop_path")]
        public string? BackdropPath { get; init; }
    }

    private sealed record TmdbMovie
    {
        public int Id { get; init; }
        public required string Title { get; init; }
        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; init; }
        public string? Overview { get; init; }
        [JsonPropertyName("genre_ids")]
        public List<int> GenreIds { get; init; } = [];
        [JsonPropertyName("original_language")]
        public string? OriginalLanguage { get; init; }
        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; init; }
        [JsonPropertyName("backdrop_path")]
        public string? BackdropPath { get; init; }
        [JsonPropertyName("vote_average")]
        public decimal VoteAverage { get; init; }
        [JsonPropertyName("vote_count")]
        public int VoteCount { get; init; }
    }
}
