using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Modularr.Core.PostProcessing;

// shortcut: name + size instead of the ffprobe runtime check of upstream DetectSample, upgrade once media info probing exists.
public static partial class SampleDetector
{
    // NotSampleSpecification threshold: a real episode or movie is never this small.
    public const long MaxSampleSize = 70L * 1024 * 1024;

    // DetectSample never treats streams and disc images as samples (.flv/.strm both, .iso/.img/.m2ts Radarr).
    private static readonly FrozenSet<string> NeverSampleExtensions =
        new[] { ".flv", ".strm", ".iso", ".img", ".m2ts" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?<![a-z])samples?(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SampleNameRegex { get; }

    public static bool IsSample(string path, long size) =>
        !NeverSampleExtensions.Contains(Path.GetExtension(path))
        && size < MaxSampleSize
        && (SampleNameRegex.IsMatch(Path.GetFileName(path)) || SampleNameRegex.IsMatch(Path.GetFileName(Path.GetDirectoryName(path)) ?? ""));
}
