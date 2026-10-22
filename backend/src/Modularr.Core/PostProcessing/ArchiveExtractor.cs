using System.Collections.Frozen;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.Xz;
using SharpCompress.Readers;

namespace Modularr.Core.PostProcessing;

// Volumes are every archive file consumed, nested ones included: the caller deletes them once the extraction succeeded.
public sealed record ArchiveExtraction(IReadOnlyList<string> ExtractedFiles, IReadOnlyList<string> Volumes, string? Error);

public static partial class ArchiveExtractor
{
    // Video archives are stored, not compressed: far above this ratio is an archive bomb, not a release.
    private const long MaxExpansionRatio = 100;
    private const int MaxEntries = 10_000;

    private static readonly Func<Stream, Stream> GZip = stream => new GZipStream(stream, CompressionMode.Decompress);

    private static readonly Func<Stream, Stream> BZip2 = stream =>
        BZip2Stream.Create(stream, SharpCompress.Compressors.CompressionMode.Decompress, decompressConcatenated: true, leaveOpen: false, tolerateTruncatedStream: false);

    private static readonly Func<Stream, Stream> Xz = stream => new XZStream(stream);

    // Single-stream compressors hold one file, named like the archive without its extension (.tgz and friends hold a tar).
    private static readonly FrozenDictionary<string, (Func<Stream, Stream> Open, string InnerExtension)> SingleStreams =
        new Dictionary<string, (Func<Stream, Stream> Open, string InnerExtension)>
        {
            [".gz"] = (GZip, ""),
            [".tgz"] = (GZip, ".tar"),
            [".bz2"] = (BZip2, ""),
            [".tbz2"] = (BZip2, ".tar"),
            [".tb2"] = (BZip2, ".tar"),
            [".xz"] = (Xz, ""),
            [".txz"] = (Xz, ".tar"),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

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

    // Extracts every archive among files next to itself, then the archives found inside, up to MaxArchiveDepth levels.
    // All or nothing: the size and entry caps hold over the whole tree, and on failure everything written is removed.
    public static ArchiveExtraction ExtractAll(IReadOnlyList<string> files, PostProcessingOptions options)
    {
        bool IsArchive(string file) => FileClassifier.Classify(file, options) == FileKind.Archive;

        Budget budget = new(files.Where(IsArchive).Sum(f => new FileInfo(f).Length) * MaxExpansionRatio);
        List<string> written = [];
        List<string> volumes = [];
        List<string> extracted = [];
        IReadOnlyList<string> pending = files;

        ArchiveExtraction Fail(string error)
        {
            RemoveAll(written);
            return new ArchiveExtraction([], [], error);
        }

        for (int depth = 0; ; depth++)
        {
            List<string> archives = [.. pending.Where(f => IsArchive(f) && IsFirstVolume(f))];

            if (archives.Count == 0)
            {
                return new ArchiveExtraction([.. extracted.Except(volumes, StringComparer.OrdinalIgnoreCase)], volumes, null);
            }

            if (depth == options.MaxArchiveDepth)
            {
                return Fail($"{Path.GetFileName(archives[0])}: archive nested deeper than {options.MaxArchiveDepth} levels");
            }

            List<string> next = [];

            foreach (string archive in archives)
            {
                if (Extract(archive, budget, written, volumes, next) is string error)
                {
                    return Fail(error);
                }
            }

            extracted.AddRange(next);
            pending = next;
        }
    }

    // Returns the error, or null once the archive's files are added to extracted. Every path created goes to written first.
    private static string? Extract(string firstVolume, Budget budget, List<string> written, List<string> volumes, List<string> extracted)
    {
        string name = Path.GetFileName(firstVolume);

        try
        {
            string? error = SingleStreams.TryGetValue(Path.GetExtension(firstVolume), out (Func<Stream, Stream> Open, string InnerExtension) single)
                ? Decompress(firstVolume, single.Open, single.InnerExtension, budget, written, volumes, extracted)
                : Unpack(firstVolume, budget, written, volumes, extracted);

            return error is null ? null : $"{name}: {error}";
        }
        catch (CryptographicException)
        {
            return $"{name}: password protected archive";
        }
        catch (Exception ex) when (ex is SharpCompressException or InvalidDataException or EndOfStreamException)
        {
            return $"{name}: corrupt archive ({ex.Message})";
        }
        catch (IOException ex)
        {
            // Disk full or a header that lied about sizes: still a verdict, never an unhandled throw.
            return $"{name}: extraction failed ({ex.Message})";
        }
    }

    private static string? Decompress(string archive, Func<Stream, Stream> open, string innerExtension, Budget budget, List<string> written, List<string> volumes, List<string> extracted)
    {
        string target = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(archive))!, Path.GetFileNameWithoutExtension(archive) + innerExtension);

        if (File.Exists(target) || Directory.Exists(target))
        {
            return "would overwrite an existing file";
        }

        written.Add(target);
        volumes.Add(archive);

        using (FileStream input = File.OpenRead(archive))
        using (Stream source = open(input))
        {
            if (!Copy(source, target, budget))
            {
                return "archive too large, it would expand far beyond its own size";
            }
        }

        if (!string.Equals(Path.GetExtension(target), ".tar", StringComparison.OrdinalIgnoreCase))
        {
            extracted.Add(target);
            return null;
        }

        // A compressed tar is one archive, not one nested in another: it does not use a depth level.
        return Unpack(target, budget, written, volumes, extracted);
    }

    // Every entry is checked before anything is written, nothing is overwritten.
    private static string? Unpack(string firstVolume, Budget budget, List<string> written, List<string> volumes, List<string> extracted)
    {
        List<FileInfo> parts = [.. ArchiveFactory.GetFileParts(new FileInfo(firstVolume))];
        string destination = Path.GetDirectoryName(Path.GetFullPath(firstVolume))!;
        string root = Path.TrimEndingDirectorySeparator(destination) + Path.DirectorySeparatorChar;

        using IArchive archive = ArchiveFactory.OpenArchive(parts, new ReaderOptions());

        if (archive.IsEncrypted || archive.Entries.Any(e => e.IsEncrypted))
        {
            return "password protected archive";
        }

        if (!archive.IsComplete)
        {
            return "incomplete archive, a volume is missing";
        }

        HashSet<string> targets = new(StringComparer.Ordinal);
        long declaredSize = 0;

        foreach (IArchiveEntry entry in archive.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(root, entry.Key ?? ""));

            if (!target.StartsWith(root, StringComparison.Ordinal))
            {
                return $"entry '{entry.Key}' escapes the download folder";
            }

            if (CrossesLink(target, destination))
            {
                return $"entry '{entry.Key}' would be written through a link";
            }

            if (entry.IsDirectory)
            {
                written.Add(NewRoot(target, destination));
                continue;
            }

            declaredSize += Math.Max(entry.Size, 0);

            if (--budget.Entries < 0 || declaredSize > budget.Bytes)
            {
                return "archive too large, it would expand far beyond its own size";
            }

            if (entry.LinkTarget is not null)
            {
                return $"link entry '{entry.Key}' refused";
            }

            // A single-file download sits in the shared downloads root: never replace another download's file.
            if (File.Exists(target) || Directory.Exists(target) || !targets.Add(target))
            {
                return $"entry '{entry.Key}' would overwrite an existing file";
            }

            written.Add(NewRoot(target, destination));
        }

        volumes.AddRange(parts.Select(p => p.FullName));

        string? Write(IEntry entry, Func<Stream> open)
        {
            string target = Path.GetFullPath(Path.Combine(root, entry.Key ?? ""));

            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(target);
                return null;
            }

            if (!targets.Contains(target))
            {
                return $"entry '{entry.Key}' was not in the archive index";
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using Stream source = open();

            // Declared sizes can lie: the real output is what counts against the cap.
            if (!Copy(source, target, budget))
            {
                return "archive too large, it would expand far beyond its own size";
            }

            extracted.Add(target);
            return null;
        }

        // A solid archive is decompressed in one forward pass, not once per entry.
        if (archive.IsSolid)
        {
            using IReader reader = archive.ExtractAllEntries();

            while (reader.MoveToNextEntry())
            {
                if (Write(reader.Entry, reader.OpenEntryStream) is string error)
                {
                    return error;
                }
            }

            return null;
        }

        foreach (IArchiveEntry entry in archive.Entries)
        {
            if (Write(entry, entry.OpenEntryStream) is string error)
            {
                return error;
            }
        }

        return null;
    }

    // Stops as soon as the output goes over the remaining budget. CreateNew: an existing file is never replaced.
    private static bool Copy(Stream source, string target, Budget budget)
    {
        using FileStream output = new(target, FileMode.CreateNew);
        byte[] buffer = new byte[81_920];
        int read;

        while ((read = source.Read(buffer)) > 0)
        {
            budget.Bytes -= read;

            if (budget.Bytes < 0)
            {
                return false;
            }

            output.Write(buffer, 0, read);
        }

        return true;
    }

    // The outermost path of target that does not exist yet: deleting it undoes the write and nothing else.
    private static string NewRoot(string target, string destination)
    {
        string newRoot = target;

        for (string? parent = Path.GetDirectoryName(target); parent is not null && parent.Length > destination.Length && !Directory.Exists(parent); parent = Path.GetDirectoryName(parent))
        {
            newRoot = parent;
        }

        return newRoot;
    }

    private static void RemoveAll(List<string> written)
    {
        foreach (string path in written)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    private static bool CrossesLink(string target, string destination)
    {
        for (DirectoryInfo? directory = Directory.GetParent(target); directory is not null && directory.FullName.Length > destination.Length; directory = directory.Parent)
        {
            if (directory.LinkTarget is not null)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class Budget(long bytes)
    {
        public long Bytes { get; set; } = bytes;

        public int Entries { get; set; } = MaxEntries;
    }
}
