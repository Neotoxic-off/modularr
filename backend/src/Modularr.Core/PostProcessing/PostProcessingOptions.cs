using System.Collections.Frozen;
using Modularr.Core.MediaFiles;

namespace Modularr.Core.PostProcessing;

// Overrides must use a case-insensitive set, like the defaults.
public sealed record PostProcessingOptions
{
    // Sonarr and Radarr SubtitleFileExtensions merged (.sup is Radarr only); .txt moved to ignorable.
    public static FrozenSet<string> DefaultSubtitleExtensions { get; } = new[]
    {
        ".aqt", ".ass", ".idx", ".jss", ".psb", ".rt", ".smi", ".srt", ".ssa", ".sub", ".sup", ".utf", ".utf8", ".utf-8", ".vtt",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static FrozenSet<string> DefaultIgnorableExtensions { get; } = new[]
    {
        ".nfo", ".txt", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tbn", ".sfv", ".md5", ".srr", ".srs", ".nzb", ".par2",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> VideoExtensions { get; init; } = MediaFileExtensions.VideoExtensions;

    public IReadOnlySet<string> SubtitleExtensions { get; init; } = DefaultSubtitleExtensions;

    public IReadOnlySet<string> IgnorableExtensions { get; init; } = DefaultIgnorableExtensions;
}
