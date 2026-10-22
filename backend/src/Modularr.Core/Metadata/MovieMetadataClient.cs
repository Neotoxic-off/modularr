using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Languages;
using Modularr.Core.Parsing.Movies;

namespace Modularr.Core.Metadata;

// Port of Radarr's SkyHookProxy: movie search and lookup through api.radarr.video.
public sealed partial class MovieMetadataClient(HttpClient httpClient, IOptions<ModularrOptions> options)
{
    // shortcut: US only (Radarr's default CertificationCountry), expose all certifications once the country is configurable.
    private const string CertificationCountry = "US";

    private readonly MetadataOptions _options = options.Value.Metadata;

    // Accepts titles (optionally with a year), "imdb:tt..", "tmdb:123" and imdb.com / themoviedb.org links.
    public async Task<IReadOnlyList<MovieInfo>> SearchMoviesAsync(string term, CancellationToken cancellationToken = default)
    {
        Match link = ImdbLinkRegex.Match(term);
        if (link.Success)
        {
            term = "imdb:" + link.Groups[1].Value;
        }
        else if ((link = TmdbLinkRegex.Match(term)).Success)
        {
            term = "tmdb:" + link.Groups[1].Value;
        }

        string lowerTerm = term.ToLowerInvariant().Replace(".", "", StringComparison.Ordinal);
        string query = lowerTerm;
        string year = "";

        ParsedMovieInfo? parsed = MovieTitleParser.ParseMovieTitle(term, isDir: true);
        if (parsed is not null && parsed.PrimaryMovieTitle != term)
        {
            // Upstream replaces every period here, which breaks titles like R.I.P.D.
            query = parsed.PrimaryMovieTitle.ToLowerInvariant().Replace('.', ' ');

            if (parsed.Year > 1800)
            {
                year = parsed.Year.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (parsed.ImdbId is not null)
            {
                return await LookupAsync(ct => GetMovieByImdbAsync(parsed.ImdbId, ct), cancellationToken).ConfigureAwait(false);
            }

            if (parsed.TmdbId > 0)
            {
                return await LookupAsync(ct => GetMovieAsync(parsed.TmdbId.Value, ct), cancellationToken).ConfigureAwait(false);
            }
        }

        if (lowerTerm.StartsWith("imdb:", StringComparison.Ordinal) || lowerTerm.StartsWith("imdbid:", StringComparison.Ordinal))
        {
            string slug = lowerTerm.Split(':')[1].Trim();

            return string.IsNullOrWhiteSpace(slug) || slug.Any(char.IsWhiteSpace)
                ? []
                : await LookupAsync(ct => GetMovieByImdbAsync(slug, ct), cancellationToken).ConfigureAwait(false);
        }

        if (lowerTerm.StartsWith("tmdb:", StringComparison.Ordinal) || lowerTerm.StartsWith("tmdbid:", StringComparison.Ordinal))
        {
            string slug = lowerTerm.Split(':')[1].Trim();

            // Same id guard as the series "tvdb:" term: Radarr sent 0 and negatives to the API, which 404s them.
            return !int.TryParse(slug, NumberStyles.None, CultureInfo.InvariantCulture, out int tmdbId) || tmdbId <= 0
                ? []
                : await LookupAsync(ct => GetMovieAsync(tmdbId, ct), cancellationToken).ConfigureAwait(false);
        }

        string searchTerm = StripTrailingThe(query).Replace('_', '+').Replace(' ', '+').Replace('.', '+');
        string url = MetadataHttp.Combine(_options.RadarrApiUrl, $"search?q={Uri.EscapeDataString(searchTerm)}&year={year}");
        List<MovieResource> movies = await MetadataHttp.GetJsonAsync<List<MovieResource>>(httpClient, url, cancellationToken).ConfigureAwait(false)
            ?? throw new MetadataSourceException($"Search for '{term}' failed: RadarrAPI answered 404.");

        return [.. movies.Select(MapMovie)];
    }

    public async Task<MovieInfo> GetMovieAsync(int tmdbId, CancellationToken cancellationToken = default)
    {
        string id = tmdbId.ToString(CultureInfo.InvariantCulture);
        MovieResource movie = await MetadataHttp.GetJsonAsync<MovieResource>(httpClient, MetadataHttp.Combine(_options.RadarrApiUrl, $"movie/{id}"), cancellationToken).ConfigureAwait(false)
            ?? throw new MetadataNotFoundException(id);

        return MapMovie(movie);
    }

    // Accepts "tt123", "123" or "tt0000123"; an id that cannot be normalized is not found, like a 404.
    public async Task<MovieInfo> GetMovieByImdbAsync(string imdbId, CancellationToken cancellationToken = default)
    {
        string normalized = MovieTitleParser.NormalizeImdbId(imdbId) ?? throw new MetadataNotFoundException(imdbId);
        List<MovieResource>? movies = await MetadataHttp.GetJsonAsync<List<MovieResource>>(httpClient,
            MetadataHttp.Combine(_options.RadarrApiUrl, $"movie/imdb/{normalized}"), cancellationToken).ConfigureAwait(false);

        return movies is [MovieResource first, ..] ? MapMovie(first) : throw new MetadataNotFoundException(normalized);
    }

    private static async Task<IReadOnlyList<MovieInfo>> LookupAsync(Func<CancellationToken, Task<MovieInfo>> lookup, CancellationToken cancellationToken)
    {
        try
        {
            return [await lookup(cancellationToken).ConfigureAwait(false)];
        }
        catch (MetadataNotFoundException)
        {
            return [];
        }
    }

    private static string StripTrailingThe(string title) =>
        title.EndsWith(",the", StringComparison.Ordinal) ? title[..^4]
        : title.EndsWith(", the", StringComparison.Ordinal) ? title[..^5]
        : title;

    private static MovieInfo MapMovie(MovieResource movie)
    {
        DateTime now = DateTime.UtcNow;
        MovieStatus status = MovieStatus.Announced;

        if (movie.InCinema is { } inCinema && now > inCinema)
        {
            status = movie.PhysicalRelease is null && movie.DigitalRelease is null && now > inCinema.AddDays(90)
                ? MovieStatus.Released
                : MovieStatus.InCinemas;
        }

        if (movie.PhysicalRelease <= now || movie.DigitalRelease <= now)
        {
            status = MovieStatus.Released;
        }

        MovieRatings ratings = MapRatings(movie.MovieRatings);
        // RadarrAPI's deprecated flat list still carries the TMDB rating for entries cached before MovieRatings existed.
        if (ratings.Tmdb is null && movie.Ratings is [RatingItem tmdb, ..])
        {
            ratings = ratings with { Tmdb = new MetadataRating(tmdb.Count, tmdb.Value) };
        }

        return new MovieInfo
        {
            TmdbId = movie.TmdbId,
            ImdbId = movie.ImdbId,
            Title = movie.Title,
            OriginalTitle = movie.OriginalTitle,
            OriginalLanguage = (movie.OriginalLanguage is null ? null : IsoLanguages.Find(movie.OriginalLanguage)?.Language) ?? Language.English,
            Overview = movie.Overview,
            Year = movie.Year,
            SecondaryYear = movie.Premier is { } premier && premier.Year != movie.Year ? premier.Year : null,
            InCinemas = movie.InCinema,
            PhysicalRelease = movie.PhysicalRelease,
            DigitalRelease = movie.DigitalRelease,
            Status = status,
            Runtime = movie.Runtime ?? 0,
            Popularity = movie.Popularity ?? 0,
            Certification = movie.Certifications.FirstOrDefault(c => c.Country == CertificationCountry)?.Certification,
            Ratings = ratings,
            Genres = movie.Genres ?? [],
            Keywords = movie.Keywords ?? [],
            Images = [.. movie.Images.Select(i => MetadataImage.From(i.CoverType, i.Url))],
            AlternativeTitles = [.. movie.AlternativeTitles.Select(t => t.Title)],
            Translations = [.. movie.Translations.Select(t => new MovieTranslation(
                t.Language is null ? null : IsoLanguages.Find(t.Language)?.Language, t.Title, t.Overview))],
            RecommendationTmdbIds = [.. movie.Recommendations?.Select(r => r.TmdbId) ?? []],
            Website = movie.Homepage,
            YouTubeTrailerId = movie.YoutubeTrailerId,
            Studio = movie.Studio,
            CollectionTmdbId = movie.Collection?.TmdbId,
            CollectionTitle = movie.Collection?.Name,
        };
    }

    private static MovieRatings MapRatings(RatingResource? ratings) => ratings is null ? new() : new()
    {
        Tmdb = MapRating(ratings.Tmdb),
        Imdb = MapRating(ratings.Imdb),
        Metacritic = MapRating(ratings.Metacritic),
        RottenTomatoes = MapRating(ratings.RottenTomatoes),
        Trakt = MapRating(ratings.Trakt),
    };

    private static MetadataRating? MapRating(RatingItem? rating) => rating is null ? null : new MetadataRating(rating.Count, rating.Value);

    [GeneratedRegex(@"\bimdb\.com/title/(tt\d{7,})\b", RegexOptions.IgnoreCase)]
    private static partial Regex ImdbLinkRegex { get; }

    [GeneratedRegex(@"\bthemoviedb\.org/movie/(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TmdbLinkRegex { get; }

    private sealed record MovieResource
    {
        public int TmdbId { get; init; }
        public string? ImdbId { get; init; }
        public string? Overview { get; init; }
        public required string Title { get; init; }
        public string? OriginalTitle { get; init; }
        public List<RatingItem>? Ratings { get; init; }
        public RatingResource? MovieRatings { get; init; }
        public int? Runtime { get; init; }
        public List<ImageResource> Images { get; init; } = [];
        public List<string>? Genres { get; init; }
        public List<string>? Keywords { get; init; }
        public int Year { get; init; }
        public DateTime? Premier { get; init; }
        public DateTime? InCinema { get; init; }
        public DateTime? PhysicalRelease { get; init; }
        public DateTime? DigitalRelease { get; init; }
        public List<AlternativeTitleResource> AlternativeTitles { get; init; } = [];
        public List<TranslationResource> Translations { get; init; } = [];
        public string? Studio { get; init; }
        public string? YoutubeTrailerId { get; init; }
        public List<CertificationResource> Certifications { get; init; } = [];
        public CollectionResource? Collection { get; init; }
        public string? OriginalLanguage { get; init; }
        public string? Homepage { get; init; }
        public List<RecommendationResource>? Recommendations { get; init; }
        public float? Popularity { get; init; }
    }

    private sealed record RatingResource(RatingItem? Tmdb, RatingItem? Imdb, RatingItem? Metacritic, RatingItem? RottenTomatoes, RatingItem? Trakt);

    private sealed record RatingItem(int Count, decimal Value);

    private sealed record ImageResource(string? CoverType, string Url);

    private sealed record AlternativeTitleResource(string Title);

    private sealed record TranslationResource(string? Title, string? Overview, string? Language);

    private sealed record CertificationResource(string? Country, string? Certification);

    private sealed record CollectionResource(int TmdbId, string? Name);

    private sealed record RecommendationResource(int TmdbId);
}
