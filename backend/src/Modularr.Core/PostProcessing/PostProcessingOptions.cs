using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using Modularr.Core.MediaFiles;

namespace Modularr.Core.PostProcessing;

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

    public IReadOnlySet<string> VideoExtensions { get; init; } = MediaFileExtensions.VideoExtensions;

    public IReadOnlySet<string> SubtitleExtensions { get; init; } = DefaultSubtitleExtensions;

    public IReadOnlySet<string> IgnorableExtensions { get; init; } = DefaultIgnorableExtensions;

    // Path.GetExtension returns the leading dot, so an entry without one could never match.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        new (string Name, IReadOnlySet<string> Extensions)[] { (nameof(VideoExtensions), VideoExtensions), (nameof(SubtitleExtensions), SubtitleExtensions), (nameof(IgnorableExtensions), IgnorableExtensions) }
            .SelectMany(list => list.Extensions
                .Where(extension => extension.Length < 2 || extension[0] != '.')
                .Select(extension => new ValidationResult($"'{extension}' must start with a dot, e.g. '.mkv'.", [list.Name])));
}
