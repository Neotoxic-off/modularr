using Modularr.Core.Decisions;
using Modularr.Core.Indexers;
using Modularr.Core.Languages;
using Modularr.Core.Parsing;
using Modularr.Core.Parsing.Episodes;
using Modularr.Core.Parsing.Movies;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;

namespace Modularr.Core.Search;

// Upstream ParsingService.Map + DownloadDecisionMaker: parse a search result, map it to the library, then run the specifications.
public static class ReleaseDecisions
{
    // shortcut: titles are matched on their clean form only (no aliases or scene mappings), add them with a scene mapping source.
    // searchSeason/searchEpisode: an interactive season or episode search (upstream SeasonMatch + SingleEpisodeSearchMatch).
    public static DownloadDecision ForSeries(
        ReleaseInfo release, QualityProfile profile, Series? series, IReadOnlyDictionary<int, MediaFile> files, DecisionContext context, int? searchSeason = null, int? searchEpisode = null)
    {
        ArgumentNullException.ThrowIfNull(release);
        ParsedEpisodeInfo? parsed = EpisodeParser.ParseTitle(release.Title);
        IReadOnlyList<EpisodeTarget> targets = parsed is null ? []
            : series is null ? [.. parsed.SeasonNumbers.SelectMany(season => parsed.EpisodeNumbers.Select(number => new EpisodeTarget { SeasonNumber = season, EpisodeNumber = number }))]
            : [.. MapEpisodes(parsed, series.Episodes).Select(episode => Target(episode, files))];
        Episode? pilot = series?.Episodes.Where(e => e.SeasonNumber > 0).OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).FirstOrDefault();

        ReleaseCandidate candidate = new()
        {
            Release = release,
            MediaType = series?.MediaType ?? MediaType.Series,
            Profile = profile,
            Quality = parsed?.Quality ?? QualityParser.ParseQuality(release.Title),
            // Only what follows the title describes the release: a title can name a language ("The Spanish Princess").
            Languages = LanguageParser.ParseReleaseLanguages(parsed?.ReleaseTokens ?? release.Title),
            ReleaseGroup = parsed?.ReleaseGroup ?? ReleaseGroupParser.ParseReleaseGroup(release.Title),
            Runtime = series?.Runtime ?? 0,
            OriginalLanguage = series?.OriginalLanguage ?? Language.Unknown,
            Episodes = targets,
            FullSeason = parsed?.FullSeason ?? false,
            MultiSeason = parsed?.IsMultiSeason ?? false,
            SplitEpisode = parsed?.IsSplitEpisode ?? false,
            Special = parsed?.Special ?? false,
            Pilot = pilot is null ? null : Target(pilot, files),
        };

        if (parsed is null)
        {
            return Reject(candidate, DownloadRejectionReason.UnableToParse, "Unable to parse release");
        }

        string expected = ReleaseTitle.CleanTitle(series?.Title ?? "");
        if (series is not null && ReleaseTitle.CleanTitle(parsed.SeriesTitle) != expected && ReleaseTitle.CleanTitle(parsed.SeriesTitleInfo.TitleWithoutYear) != expected)
        {
            return Reject(candidate, DownloadRejectionReason.UnknownSeries, $"'{parsed.SeriesTitle}' is not {series.Title}");
        }

        if (series is not null && targets.Count == 0)
        {
            return Reject(candidate, DownloadRejectionReason.UnknownEpisode, "Unable to identify correct episode(s) using release name");
        }

        if (searchSeason is int season && !(parsed.SeasonNumbers.Count > 0 ? parsed.SeasonNumbers.Contains(season) : targets.Any(t => t.SeasonNumber == season)))
        {
            return Reject(candidate, DownloadRejectionReason.WrongSeason, "Wrong season");
        }

        if (searchSeason is not null && searchEpisode is int episode)
        {
            if (parsed.EpisodeNumbers.Count == 0)
            {
                return Reject(candidate, DownloadRejectionReason.FullSeason, "Full season pack");
            }

            if (!parsed.EpisodeNumbers.Contains(episode))
            {
                return Reject(candidate, DownloadRejectionReason.WrongEpisode, "Wrong episode");
            }
        }

        return DownloadDecisionMaker.GetDecision(candidate, context);
    }

    public static DownloadDecision ForMovie(ReleaseInfo release, QualityProfile profile, Movie? movie, IEnumerable<MediaFile> files, DecisionContext context)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(profile);
        ParsedMovieInfo? parsed = MovieTitleParser.ParseMovieTitle(release.Title);
        MediaFile? best = files.OrderByDescending(f => f.Quality, Comparer<QualityModel>.Create((a, b) => profile.Compare(a, b))).FirstOrDefault();

        ReleaseCandidate candidate = new()
        {
            Release = release,
            MediaType = MediaType.Movie,
            Profile = profile,
            Quality = parsed?.Quality ?? QualityParser.ParseQuality(release.Title),
            // The title is replaced by a placeholder there: "French Kiss" names no audio track.
            Languages = LanguageParser.ParseReleaseLanguages(parsed?.SimpleReleaseTitle ?? release.Title),
            ReleaseGroup = parsed?.ReleaseGroup ?? ReleaseGroupParser.ParseReleaseGroup(release.Title),
            Runtime = movie?.Runtime ?? 0,
            OriginalLanguage = movie?.OriginalLanguage ?? Language.Unknown,
            MovieFile = best is null ? null : new ExistingFile(best.Quality, best.ReleaseGroup),
        };

        if (parsed is null)
        {
            return Reject(candidate, DownloadRejectionReason.UnableToParse, "Unable to parse release");
        }

        // Radarr accepts the year either side of the release year (festival vs cinema release).
        if (movie is not null
            && (!parsed.MovieTitles.Any(t => MovieTitleParser.CleanMovieTitle(t) == MovieTitleParser.CleanMovieTitle(movie.Title))
                || (parsed.Year is int year && movie.Year > 0 && Math.Abs(year - movie.Year) > 1)))
        {
            return Reject(candidate, DownloadRejectionReason.WrongMovie, $"'{parsed.PrimaryMovieTitle} ({parsed.Year})' is not {movie.Title} ({movie.Year})");
        }

        return DownloadDecisionMaker.GetDecision(candidate, context);
    }

    // The library episodes a parsed release covers: by air date, whole seasons, scene numbers, or absolute numbers for anime.
    public static IReadOnlyList<Episode> MapEpisodes(ParsedEpisodeInfo parsed, IEnumerable<Episode> episodes)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        return parsed switch
        {
            { IsDaily: true } => [.. episodes.Where(e => e.AirDate == parsed.AirDate)],
            { FullSeason: true } => [.. episodes.Where(e => parsed.SeasonNumbers.Contains(e.SeasonNumber))],
            { EpisodeNumbers.Count: 0, IsAbsoluteNumbering: true } =>
                [.. episodes.Where(e => e.AbsoluteEpisodeNumber is int number && parsed.AbsoluteEpisodeNumbers.Contains(number))],
            _ => [.. episodes.Where(e => parsed.SeasonNumbers.Contains(e.SeasonNumber) && parsed.EpisodeNumbers.Contains(e.EpisodeNumber))],
        };
    }

    private static EpisodeTarget Target(Episode episode, IReadOnlyDictionary<int, MediaFile> files) => new()
    {
        SeasonNumber = episode.SeasonNumber,
        EpisodeNumber = episode.EpisodeNumber,
        AirDate = episode.AirDate is DateOnly airDate ? new DateTimeOffset(airDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
        File = episode.MediaFileId is int fileId && files.TryGetValue(fileId, out MediaFile? file) ? new ExistingFile(file.Quality, file.ReleaseGroup) : null,
    };

    private static DownloadDecision Reject(ReleaseCandidate candidate, DownloadRejectionReason reason, string message) =>
        new(candidate, [new Rejection(reason, message)]);
}
