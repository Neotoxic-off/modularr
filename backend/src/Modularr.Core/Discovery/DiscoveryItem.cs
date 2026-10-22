using Modularr.Core.Decisions;
using Modularr.Core.Languages;
using Modularr.Core.Metadata;

namespace Modularr.Core.Discovery;

// A movie or show as /api/lookup and /api/discover return it. Kind is Anime for anime series; TmdbId identifies a movie, TvdbId a series.
public sealed record DiscoveryItem
{
    public required MediaType Kind { get; init; }
    public int? TmdbId { get; init; }
    public int? TvdbId { get; init; }
    public string? ImdbId { get; init; }
    public required string Title { get; init; }
    public int? Year { get; init; }
    public string? Overview { get; init; }
    public string? Status { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];

    // The original language's name, when the source gives it.
    public string? Language { get; init; }
    public string? Poster { get; init; }
    public string? Backdrop { get; init; }

    // 0-10.
    public decimal? Rating { get; init; }
    public int? LibraryId { get; init; }
    public bool InLibrary => LibraryId is not null;

    // In the library with a file: the movie's, or any episode's.
    public bool HasFile { get; init; }

    public static DiscoveryItem From(MovieInfo movie) => new()
    {
        Kind = MediaType.Movie,
        TmdbId = movie.TmdbId,
        ImdbId = movie.ImdbId,
        Title = movie.Title,
        Year = movie.Year > 0 ? movie.Year : null,
        Overview = movie.Overview,
        Status = movie.Status.ToString(),
        Genres = movie.Genres,
        Language = KnownName(movie.OriginalLanguage),
        Poster = movie.Images.UrlOf(MediaCoverType.Poster),
        Backdrop = movie.Images.UrlOf(MediaCoverType.Fanart),
        Rating = movie.Ratings.Rating,
    };

    public static DiscoveryItem From(SeriesInfo series) => new()
    {
        Kind = series.IsAnime ? MediaType.Anime : MediaType.Series,
        TvdbId = series.TvdbId,
        ImdbId = series.ImdbId,
        Title = series.Title,
        Year = series.Year,
        Overview = series.Overview,
        Status = series.Status.ToString(),
        Genres = series.Genres,
        Language = KnownName(series.OriginalLanguage),
        Poster = series.Images.UrlOf(MediaCoverType.Poster),
        Backdrop = series.Images.UrlOf(MediaCoverType.Fanart),
        Rating = series.Rating,
    };

    private static string? KnownName(Language language) => language.Id > 0 ? language.Name : null;
}

// An unreachable source gives an empty list and its error, never a failed request.
public sealed record DiscoveryResult(IReadOnlyList<DiscoveryItem> Items, string? Error = null);
