using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Modularr.Core.PostProcessing;

public static partial class FileClassifier
{
    // Sonarr/Radarr FileExtensions (identical) plus xz; .tar.gz/.tar.bz2 dropped, Path.GetExtension only returns the last part.
    public static FrozenSet<string> ArchiveExtensions { get; } = new[]
    {
        ".7z", ".bz2", ".gz", ".r00", ".rar", ".tar", ".tb2", ".tbz2", ".tgz", ".txz", ".xz", ".zip",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static FrozenSet<string> DangerousExtensions { get; } = new[]
    {
        ".arj", ".lnk", ".lzh", ".ps1", ".scr", ".vbs", ".zipx",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static FrozenSet<string> ExecutableExtensions { get; } = new[]
    {
        ".bat", ".cmd", ".exe", ".sh",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // DiskScanService.ExcludedFilesRegex: OS and tool junk.
    [GeneratedRegex(@"^\.(?:_|unmanic|DS_Store$)|^Thumbs\.db$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JunkFileRegex { get; }

    // Dangerous and executable win over the configurable lists: an override must never let an .exe through.
    public static FileKind Classify(string path, PostProcessingOptions options)
    {
        string fileName = Path.GetFileName(path);
        string extension = Path.GetExtension(path);

        return extension switch
        {
            _ when DangerousExtensions.Contains(extension) => FileKind.Dangerous,
            _ when ExecutableExtensions.Contains(extension) => FileKind.Executable,
            _ when ArchiveExtensions.Contains(extension) || ArchiveExtractor.RarVolumeRegex.IsMatch(fileName) => FileKind.Archive,
            _ when options.VideoExtensions.Contains(extension) => FileKind.Video,
            _ when options.SubtitleExtensions.Contains(extension) => FileKind.Subtitle,
            _ when options.IgnorableExtensions.Contains(extension) || JunkFileRegex.IsMatch(fileName) => FileKind.Ignorable,
            _ => FileKind.Disallowed,
        };
    }
}
