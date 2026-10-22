using Modularr.Core.Decisions;
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
    public string? Poster { get; init; }
    public string? Backdrop { get; init; }

    // 0-10.
    public decimal? Rating { get; init; }
    public int? LibraryId { get; init; }
    public bool InLibrary => LibraryId is not null;

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
        Poster = series.Images.UrlOf(MediaCoverType.Poster),
        Backdrop = series.Images.UrlOf(MediaCoverType.Fanart),
        Rating = series.Rating,
    };
}

// An unreachable source gives an empty list and its error, never a failed request.
public sealed record DiscoveryResult(IReadOnlyList<DiscoveryItem> Items, string? Error = null);
