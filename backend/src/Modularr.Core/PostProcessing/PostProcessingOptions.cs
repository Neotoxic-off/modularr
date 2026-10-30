using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using Modularr.Core.MediaFiles;

namespace Modularr.Core.PostProcessing;

// How a finished download reaches the library.
public enum ImportMode
{
    // The files leave the downloads folder and the torrent is removed: no seeding, no space used twice.
    Move,

    // The files are linked (copied across volumes) and the torrent keeps seeding.
    Hardlink,
}

// Overrides must use a case-insensitive set, like the defaults.
public sealed record PostProcessingOptions : IValidatableObject
{
    // Sonarr and Radarr SubtitleFileExtensions merged (.sup is Radarr only); .txt moved to ignorable.
    public static FrozenSet<string> DefaultSubtitleExtensions { get; } = new[]
    {
        ".aqt", ".ass", ".idx", ".jss", ".psb", ".rt", ".smi", ".srt", ".ssa", ".sub", ".sup", ".utf", ".utf8", ".utf-8", ".vtt",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static FrozenSet<string> DefaultIgnorableExtensions { get; } = new[]
    {
        ".nfo", ".txt", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tbn", ".sfv", ".md5", ".srr", ".srs", ".nzb", ".par2",
        ".url", ".diz", ".htm", ".html", ".log", ".xml",
        // External audio tracks are not imported, but they belong to a legit release: delete without blacklisting.
        ".mka", ".ac3", ".dts", ".flac",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public ImportMode ImportMode { get; init; } = ImportMode.Move;

    // Imported files get a clean "Series - S01E01 - Title [Quality]" or "Movie (Year) [Quality]" name instead of the release's.
    public bool RenameFiles { get; init; } = true;

    // Files replaced by an upgrade are moved here; empty deletes them.
    public string RecycleBinPath { get; init; } = "";

    public IReadOnlySet<string> VideoExtensions { get; init; } = MediaFileExtensions.VideoExtensions;

    public IReadOnlySet<string> SubtitleExtensions { get; init; } = DefaultSubtitleExtensions;

    public IReadOnlySet<string> IgnorableExtensions { get; init; } = DefaultIgnorableExtensions;

    // Archive levels extracted, 2 = an archive inside an archive. Deeper nesting rejects the release.
    [Range(1, 10)]
    public int MaxArchiveDepth { get; init; } = 2;

    // Path.GetExtension returns the leading dot, so an entry without one could never match.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        new (string Name, IReadOnlySet<string> Extensions)[] { (nameof(VideoExtensions), VideoExtensions), (nameof(SubtitleExtensions), SubtitleExtensions), (nameof(IgnorableExtensions), IgnorableExtensions) }
            .SelectMany(list => list.Extensions
                .Where(extension => extension.Length < 2 || extension[0] != '.')
                .Select(extension => new ValidationResult($"'{extension}' must start with a dot, e.g. '.mkv'.", [list.Name])));
}
