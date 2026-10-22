using System.Globalization;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Modularr.Core.PostProcessing;

public sealed record ArchiveExtraction(IReadOnlyList<string> ExtractedFiles, IReadOnlyList<string> Volumes, string? Error);

public static partial class ArchiveExtractor
{
    [GeneratedRegex(@"\.part(?<number>\d+)\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RarPartRegex { get; }

    // Old style rar volumes after the .rar: .r00, .r01 ... .r999.
    [GeneratedRegex(@"\.r\d{2,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex RarVolumeRegex { get; }

    // Later volumes are opened through their first one.
    public static bool IsFirstVolume(string path)
    {
        string fileName = Path.GetFileName(path);
        Match part = RarPartRegex.Match(fileName);

        return part.Success
            ? int.Parse(part.Groups["number"].ValueSpan, CultureInfo.InvariantCulture) == 1
            : !RarVolumeRegex.IsMatch(fileName);
    }

    // Extracts next to the archive, all or nothing: every entry is checked before anything is written.
    public static ArchiveExtraction Extract(string firstVolume)
    {
        List<FileInfo> volumes = [.. ArchiveFactory.GetFileParts(new FileInfo(firstVolume))];
        string[] volumePaths = [.. volumes.Select(v => v.FullName)];
        string destination = Path.GetDirectoryName(Path.GetFullPath(firstVolume))!;
        string root = Path.TrimEndingDirectorySeparator(destination) + Path.DirectorySeparatorChar;
        string name = Path.GetFileName(firstVolume);

        ArchiveExtraction Fail(string reason) => new([], volumePaths, $"{name}: {reason}");

        try
        {
            using IArchive archive = ArchiveFactory.OpenArchive(volumes, new ReaderOptions());

            if (archive.IsEncrypted || archive.Entries.Any(e => e.IsEncrypted))
            {
                return Fail("password protected archive");
            }

            if (!archive.IsComplete)
            {
                return Fail("incomplete archive, a volume is missing");
            }

            List<string> extracted = [];

            foreach (IArchiveEntry entry in archive.Entries.Where(e => !e.IsDirectory))
            {
                if (entry.LinkTarget is not null)
                {
                    return Fail($"link entry '{entry.Key}' refused");
                }

                string target = Path.GetFullPath(Path.Combine(root, entry.Key ?? ""));

                if (!target.StartsWith(root, StringComparison.Ordinal))
                {
                    return Fail($"entry '{entry.Key}' escapes the download folder");
                }

                extracted.Add(target);
            }

            archive.WriteToDirectory(destination, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });

            return new ArchiveExtraction(extracted, volumePaths, null);
        }
        catch (CryptographicException)
        {
            return Fail("password protected archive");
        }
        catch (Exception ex) when (ex is SharpCompressException or EndOfStreamException)
        {
            return Fail($"corrupt archive ({ex.Message})");
        }
    }
}
