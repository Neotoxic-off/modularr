using Microsoft.EntityFrameworkCore;
using Modularr.Core.Languages;
using Modularr.Core.Persistence;
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

        // The library by title (the Subtitles page): coverage per wanted language, then a title's files one by one.
        api.MapGet("/subtitles/overview", (SubtitleService subtitles, CancellationToken ct) => subtitles.OverviewAsync(ct));
        api.MapGet("/subtitles/series/{id:int}", async (int id, SubtitleService subtitles, CancellationToken ct) => new { seasons = await subtitles.SeriesAsync(id, ct) });
        api.MapGet("/subtitles/movies/{id:int}", async (int id, SubtitleService subtitles, CancellationToken ct) => new { languages = await subtitles.MovieAsync(id, ct) });

        // Every file of the title through the runner, which skips those missing nothing.
        api.MapPost("/subtitles/series/{id:int}/search", async (int id, ModularrDbContext db, SubtitleRunner runner, CancellationToken ct) =>
            Enqueue(await db.MediaFiles.Where(f => f.SeriesId == id).Select(f => f.Id).ToListAsync(ct), runner));
        api.MapPost("/subtitles/movies/{id:int}/search", async (int id, ModularrDbContext db, SubtitleRunner runner, CancellationToken ct) =>
            Enqueue(await db.MediaFiles.Where(f => f.MovieId == id).Select(f => f.Id).ToListAsync(ct), runner));
        return api;
    }

    private static IResult Enqueue(List<int> files, SubtitleRunner runner)
    {
        files.ForEach(id => runner.Enqueue(id, reportMissing: false));
        return Results.Accepted();
    }

    // The file name only: the folder is the title's.
    private static object ToDto(SubtitleFile file) => new { file.Language, file.HearingImpaired, file.Forced, file = Path.GetFileName(file.Path) };
}
