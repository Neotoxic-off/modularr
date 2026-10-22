using Modularr.Core.Indexers;
using Modularr.Core.Languages;
using Modularr.Core.Qualities;

namespace Modularr.Core.Decisions;

// Upstream RemoteEpisode/RemoteMovie: a search result already parsed and mapped to a series or movie of the library.
// Quality, languages, release group and the season flags come from ParsedEpisodeInfo or ParsedMovieInfo.
public sealed record ReleaseCandidate
{
    public required ReleaseInfo Release { get; init; }
    public required MediaType MediaType { get; init; }
    public required QualityProfile Profile { get; init; }
    public required QualityModel Quality { get; init; }
    public IReadOnlyList<Language> Languages { get; init; } = [];
    public string? ReleaseGroup { get; init; }

    // Series runtime (fallback for episodes without one) or movie runtime, in minutes; 0 when unknown.
    public int Runtime { get; init; }

    // Movie or series original language: what Language.Original, untagged releases and Original tracks stand for.
    public Language OriginalLanguage { get; init; } = Language.Unknown;

    // Movies only.
    public ExistingFile? MovieFile { get; init; }

    // Series and anime only: the mapped episodes and the parsed season flags.
    public IReadOnlyList<EpisodeTarget> Episodes { get; init; } = [];
    public bool FullSeason { get; init; }
    public bool MultiSeason { get; init; }
    public bool SplitEpisode { get; init; }
    public bool Special { get; init; }

    // First episode of the first regular season: lets a brand new series without a runtime yet pass the size check.
    public EpisodeTarget? Pilot { get; init; }

    public IEnumerable<ExistingFile> ExistingFiles =>
        MediaType == MediaType.Movie
            ? MovieFile is null ? [] : [MovieFile]
            : Episodes.Select(e => e.File).OfType<ExistingFile>();

    public override string ToString() => $"{Release.Title} ({Quality})";
}
