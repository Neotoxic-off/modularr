using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Languages;
using Modularr.Core.Persistence;

namespace Modularr.Core.Subtitles;

// The subtitles of an imported file: which wanted languages are next to it, the providers' candidates, and the downloads.
public sealed record SubtitleStatus(IReadOnlyList<SubtitleFile> Present, IReadOnlyList<Language> Missing);

public sealed class SubtitleService(ModularrDbContext db, ActivityLog activities, IOptionsMonitor<ModularrOptions> options, IEnumerable<ISubtitleProvider> providers)
{
    // A periodic retry asks the providers for at most this many files.
    public const int RetryBatch = 20;

    // ...and looks at most at this many files to find them.
    public const int RetryScan = 200;
    public static readonly TimeSpan RetryInterval = TimeSpan.FromHours(6);

    private IEnumerable<ISubtitleProvider> Enabled => providers.Where(p => p.Enabled);

    private IReadOnlyList<string> Roots => [options.CurrentValue.Paths.Series, options.CurrentValue.Paths.Movies, options.CurrentValue.Paths.Anime];

    public async Task<SubtitleStatus> StatusAsync(int? movieId, int? episodeId, CancellationToken cancellationToken)
    {
        Target target = await TargetAsync(movieId, episodeId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SubtitleFile> present = SubtitleFiles.Present(target.File.Path, Roots);
        return new SubtitleStatus(present, Missing(target, present));
    }

    // Every enabled provider's candidates for one language, or for each wanted language; best first.
    public async Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(int? movieId, int? episodeId, Language? language, CancellationToken cancellationToken)
    {
        Target target = await TargetAsync(movieId, episodeId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Language> languages = language is null ? target.Wanted : [EnsureReal(language)];
        if (languages.Count == 0)
        {
            throw new ArgumentException("No subtitle language is wanted for this title: pick one.", nameof(language));
        }

        List<SubtitleCandidate> found = [];
        foreach (ISubtitleProvider provider in Enabled)
        {
            found.AddRange(await provider.SearchAsync(Query(target, languages), cancellationToken).ConfigureAwait(false));
        }

        return Rank(target, found, languages);
    }

    // A candidate from SearchAsync, saved next to the video. Never replaces a subtitle already there.
    public async Task<SubtitleFile> DownloadAsync(int? movieId, int? episodeId, SubtitleCandidate candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        EnsureReal(candidate.Language);
        ISubtitleProvider provider = Enabled.FirstOrDefault(p => p.Name.Equals(candidate.Provider, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"'{candidate.Provider}' is not an enabled subtitle provider.", nameof(candidate));
        Target target = await TargetAsync(movieId, episodeId, cancellationToken).ConfigureAwait(false);

        SubtitleFile file = await SaveAsync(provider, target, candidate, cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException($"{target.Name} already has {candidate.Language} subtitles of that kind.", nameof(candidate));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return file;
    }

    // After an import and on the periodic retry: fetches the best candidate of each missing wanted language. Returns whether the
    // providers were asked. reportMissing records SubtitlesMissing for what is still missing (the first try only, not each retry).
    public async Task<bool> DownloadMissingAsync(int mediaFileId, bool reportMissing, CancellationToken cancellationToken)
    {
        MediaFile file = await db.MediaFiles.FirstOrDefaultAsync(f => f.Id == mediaFileId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"File {mediaFileId} is not in the library.");
        Target target = await TargetOfAsync(file, cancellationToken).ConfigureAwait(false);
        file.SubtitlesSearched = DateTime.UtcNow;
        List<Language> missing = [.. Missing(target, SubtitleFiles.Present(file.Path, Roots))];
        if (missing.Count == 0 || !Enabled.Any())
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        foreach (ISubtitleProvider provider in Enabled)
        {
            IReadOnlyList<SubtitleCandidate> candidates;
            try
            {
                candidates = Rank(target, await provider.SearchAsync(Query(target, missing), cancellationToken).ConfigureAwait(false), missing);
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                continue; // a provider that is down leaves the language for the next retry
            }

            foreach (SubtitleCandidate candidate in missing.ToList().Select(l => candidates.FirstOrDefault(c => c.Language == l && !c.Forced)).OfType<SubtitleCandidate>())
            {
                if (await TrySaveAsync(provider, target, candidate, cancellationToken).ConfigureAwait(false))
                {
                    missing.Remove(candidate.Language);
                }
            }
        }

        if (reportMissing && missing.Count > 0)
        {
            activities.Add(Activity(target, ActivityKind.SubtitlesMissing, ActivityLevel.Warning, $"No {string.Join(", ", missing)} subtitles found"));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // The files searched longest ago (never first), until RetryBatch of them needed the providers.
    // shortcut: looks at RetryScan files per run even when none of them misses a subtitle, filter in SQL on wanted languages
    // if a library outgrows it.
    public async Task<int> RetryMissingAsync(DateTime now, CancellationToken cancellationToken)
    {
        if (!Enabled.Any())
        {
            return 0;
        }

        DateTime due = now - RetryInterval;
        int[] ids = await db.MediaFiles.Where(f => f.SubtitlesSearched == null || f.SubtitlesSearched < due)
            .OrderBy(f => f.SubtitlesSearched).Select(f => f.Id).Take(RetryScan).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        int searched = 0;
        foreach (int id in ids)
        {
            if (searched == RetryBatch)
            {
                break;
            }

            try
            {
                searched += await DownloadMissingAsync(id, reportMissing: false, cancellationToken).ConfigureAwait(false) ? 1 : 0;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or KeyNotFoundException)
            {
                // A file now outside the roots or behind a link: keep its SubtitlesSearched so it stops heading the queue.
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return searched;
    }

    private async Task<bool> TrySaveAsync(ISubtitleProvider provider, Target target, SubtitleCandidate candidate, CancellationToken cancellationToken)
    {
        try
        {
            return await SaveAsync(provider, target, candidate, cancellationToken).ConfigureAwait(false) is not null;
        }
        catch (Exception ex) when (ex is HttpRequestException or ArgumentException or IOException or UnauthorizedAccessException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return false;
        }
    }

    private async Task<SubtitleFile?> SaveAsync(ISubtitleProvider provider, Target target, SubtitleCandidate candidate, CancellationToken cancellationToken)
    {
        SubtitleDownload download = await provider.DownloadAsync(candidate.Id, cancellationToken).ConfigureAwait(false);
        (string extension, byte[] content) = SubtitleFiles.Unpack(download.FileName, download.Content, options.CurrentValue.PostProcessing, out string? error)
            ?? throw new ArgumentException($"{provider.Name} sent an unusable subtitle: {error}.", nameof(candidate));
        string? path = SubtitleFiles.Save(target.File.Path, Roots, candidate.Language, candidate.HearingImpaired, candidate.Forced, extension, content);
        if (path is null)
        {
            return null;
        }

        activities.Add(Activity(target, ActivityKind.SubtitleDownloaded, ActivityLevel.Success, $"{candidate.Language} subtitles saved as {Path.GetFileName(path)}"));
        return new SubtitleFile(path, candidate.Language, candidate.HearingImpaired, candidate.Forced);
    }

    // The file's own hash first, then a release name like the file's, the wanted hearing-impaired kind, full (not forced)
    // subtitles, the wanted language order, the most downloaded.
    private IReadOnlyList<SubtitleCandidate> Rank(Target target, IEnumerable<SubtitleCandidate> candidates, IReadOnlyList<Language> languages)
    {
        string release = Path.GetFileNameWithoutExtension(target.File.Path);
        bool preferHearingImpaired = options.CurrentValue.Subtitles.PreferHearingImpaired;
        return
        [
            .. candidates
                .OrderByDescending(c => c.HashMatch)
                .ThenByDescending(c => c.Release.Equals(release, StringComparison.OrdinalIgnoreCase)
                    || (SubtitleFiles.Extensions.Contains(Path.GetExtension(c.Release)) && Path.GetFileNameWithoutExtension(c.Release).Equals(release, StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(c => c.HearingImpaired == preferHearingImpaired)
                .ThenBy(c => c.Forced)
                .ThenBy(c => IndexOf(languages, c.Language))
                .ThenByDescending(c => c.Downloads),
        ];
    }

    private static int IndexOf(IReadOnlyList<Language> languages, Language language)
    {
        for (int i = 0; i < languages.Count; i++)
        {
            if (languages[i] == language)
            {
                return i;
            }
        }

        return languages.Count;
    }

    // A forced (foreign parts only) subtitle does not cover a language.
    private static IReadOnlyList<Language> Missing(Target target, IReadOnlyList<SubtitleFile> present) =>
        [.. target.Wanted.Where(l => !present.Any(p => p.Language == l && !p.Forced))];

    // The files whose item wants subtitles, and how many of them have every wanted language on disk. One query per table; the disk
    // is read once per file.
    public async Task<(int Covered, int Total)> CoverageAsync(CancellationToken cancellationToken)
    {
        ModularrOptions settings = options.CurrentValue;
        Dictionary<int, Movie> movies = await db.Movies.AsNoTracking().ToDictionaryAsync(m => m.Id, cancellationToken).ConfigureAwait(false);
        Dictionary<int, Series> series = await db.Series.AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken).ConfigureAwait(false);
        int covered = 0;
        int total = 0;
        foreach (MediaFile file in await db.MediaFiles.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            Movie? movie = file.MovieId is int movieId ? movies.GetValueOrDefault(movieId) : null;
            Series? show = file.SeriesId is int seriesId ? series.GetValueOrDefault(seriesId) : null;
            if ((movie is null && show is null) || Wanted(settings, file, movie, show) is not { Count: > 0 } wanted)
            {
                continue;
            }

            total++;
            try
            {
                covered += Missing(new Target(file, movie, show, null, wanted), SubtitleFiles.Present(file.Path, Roots)).Count == 0 ? 1 : 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Unreadable or outside the library: not covered.
            }
        }

        return (covered, total);
    }

    // The item's wanted languages, less the audio's own when OnlyWhenAudioDiffers.
    private static IReadOnlyList<Language> Wanted(ModularrOptions settings, MediaFile file, Movie? movie, Series? series)
    {
        Language original = movie?.OriginalLanguage ?? series!.OriginalLanguage;
        IReadOnlyList<Language> wanted = movie is not null ? settings.Media.WantedSubtitles(movie) : settings.Media.WantedSubtitles(series!);
        ReleaseLanguages audio = new(file.Languages);
        return [.. wanted.Where(l => !settings.Subtitles.OnlyWhenAudioDiffers || !audio.HasAudio(l, original))];
    }

    private static Language EnsureReal(Language language) =>
        language.Id > 0 ? language : throw new ArgumentException($"{language} is not a subtitle language.", nameof(language));

    private static SubtitleQuery Query(Target target, IReadOnlyList<Language> languages)
    {
        string? hash;
        try
        {
            hash = SubtitleFiles.MovieHash(target.File.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            hash = null;
        }

        return new SubtitleQuery
        {
            Title = target.Series?.Title ?? target.Movie!.Title,
            Year = target.Series?.Year ?? target.Movie!.Year,
            ImdbId = target.Movie?.ImdbId,
            TmdbId = target.Movie?.TmdbId,
            Season = target.Episode?.SeasonNumber,
            Episode = target.Episode?.EpisodeNumber,
            MovieHash = hash,
            ReleaseName = Path.GetFileNameWithoutExtension(target.File.Path),
            Languages = languages,
        };
    }

    private async Task<Target> TargetAsync(int? movieId, int? episodeId, CancellationToken cancellationToken)
    {
        MediaFile? file = (movieId, episodeId) switch
        {
            (int movie, null) => await db.MediaFiles.Where(f => f.MovieId == movie).OrderByDescending(f => f.DateAdded).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false),
            (null, int episode) => await db.MediaFiles.Where(f => db.Episodes.Any(e => e.Id == episode && e.MediaFileId == f.Id)).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException("Give a movieId or an episodeId."),
        };
        return file is null
            ? throw new KeyNotFoundException(movieId is null ? $"Episode {episodeId} has no file." : $"Movie {movieId} has no file.")
            : await TargetOfAsync(file, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Target> TargetOfAsync(MediaFile file, CancellationToken cancellationToken)
    {
        ModularrOptions settings = options.CurrentValue;
        Movie? movie = file.MovieId is int movieId ? await db.Movies.AsNoTracking().SingleAsync(m => m.Id == movieId, cancellationToken).ConfigureAwait(false) : null;
        Series? series = file.SeriesId is int seriesId ? await db.Series.AsNoTracking().SingleAsync(s => s.Id == seriesId, cancellationToken).ConfigureAwait(false) : null;
        Episode? episode = series is null ? null
            : await db.Episodes.AsNoTracking().Where(e => e.MediaFileId == file.Id).OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return new Target(file, movie, series, episode, Wanted(settings, file, movie, series));
    }

    private static Activity Activity(Target target, ActivityKind kind, ActivityLevel level, string message) => new()
    {
        Kind = kind,
        Level = level,
        Title = target.Name,
        Message = message,
        SeriesId = target.Series?.Id,
        MovieId = target.Movie?.Id,
    };

    private sealed record Target(MediaFile File, Movie? Movie, Series? Series, Episode? Episode, IReadOnlyList<Language> Wanted)
    {
        public string Name => Episode is null
            ? Movie?.Title ?? Series!.Title
            : string.Create(CultureInfo.InvariantCulture, $"{Series!.Title} S{Episode.SeasonNumber:00}E{Episode.EpisodeNumber:00}");
    }
}
