using Modularr.Core.Languages;
using Modularr.Core.Subtitles;

namespace Modularr.Api;

// Subtitles of a movie's or an episode's file: what is there and missing, the providers' candidates, and a download.
public static class SubtitleEndpoints
{
    // No language: every wanted one.
    public sealed record SearchRequest(int? MovieId, int? EpisodeId, Language? Language = null);

    // A candidate from /subtitles/search, as it came.
    public sealed record DownloadRequest(int? MovieId, int? EpisodeId, string Provider, string Id, Language Language, bool HearingImpaired = false, bool Forced = false);

    public static RouteGroupBuilder MapSubtitleApi(this RouteGroupBuilder api)
    {
        api.MapGet("/subtitles", async (int? movieId, int? episodeId, SubtitleService subtitles, CancellationToken ct) =>
        {
            SubtitleStatus status = await subtitles.StatusAsync(movieId, episodeId, ct);
            return new { present = status.Present.Select(ToDto), missing = status.Missing };
        });
        api.MapPost("/subtitles/search", (SearchRequest request, SubtitleService subtitles, CancellationToken ct) =>
            subtitles.SearchAsync(request.MovieId, request.EpisodeId, request.Language, ct));
        api.MapPost("/subtitles/download", async (DownloadRequest request, SubtitleService subtitles, CancellationToken ct) =>
        {
            SubtitleCandidate candidate = new(request.Provider, request.Id, request.Language, Release: "", request.HearingImpaired, request.Forced, HashMatch: false, Downloads: 0);
            return ToDto(await subtitles.DownloadAsync(request.MovieId, request.EpisodeId, candidate, ct));
        });
        return api;
    }

    // The file name only: the folder is the title's.
    private static object ToDto(SubtitleFile file) => new { file.Language, file.HearingImpaired, file.Forced, file = Path.GetFileName(file.Path) };
}
