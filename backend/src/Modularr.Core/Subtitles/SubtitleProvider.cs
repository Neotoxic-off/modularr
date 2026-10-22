using Modularr.Core.Languages;

namespace Modularr.Core.Subtitles;

// What to find subtitles for: a movie (ImdbId/TmdbId) or an episode (Title of the series, Season, Episode), and the file's
// OpenSubtitles hash and release name when known.
public sealed record SubtitleQuery
{
    public required string Title { get; init; }
    public int Year { get; init; }
    public string? ImdbId { get; init; }
    public int? TmdbId { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public string? MovieHash { get; init; }
    public string? ReleaseName { get; init; }
    public required IReadOnlyList<Language> Languages { get; init; }

    public bool IsEpisode => Season is not null && Episode is not null;
}

// One subtitle a provider offers. Id is the provider's own, passed back to DownloadAsync.
public sealed record SubtitleCandidate(string Provider, string Id, Language Language, string Release, bool HearingImpaired, bool Forced, bool HashMatch, int Downloads);

// The file as the provider sent it: a subtitle or an archive holding one (SubtitleFiles.Unpack checks it).
public sealed record SubtitleDownload(string FileName, byte[] Content);

// A subtitle source. Each one talks HTTPS to its own fixed hosts only, with capped responses and a timeout.
public interface ISubtitleProvider
{
    string Name { get; }

    bool Enabled { get; }

    Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(SubtitleQuery query, CancellationToken cancellationToken);

    Task<SubtitleDownload> DownloadAsync(string id, CancellationToken cancellationToken);
}
