using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Globalization;
using Modularr.Core.Languages;
using Modularr.Core.PostProcessing;

namespace Modularr.Core.Subtitles;

// A subtitle file next to its video: "<video base>.<lang>[.hi][.forced]<ext>".
public sealed record SubtitleFile(string Path, Language Language, bool HearingImpaired, bool Forced);

// The subtitle files of the library: naming, the checks on what a provider sent, and the reads and writes next to a video.
// Only inside the library roots, never through a link or reparse point, never overwriting, moving or deleting a library file.
public static class SubtitleFiles
{
    public const int MaxBytes = 2 * 1024 * 1024;

    public static FrozenSet<string> Extensions { get; } = new[] { ".srt", ".ass", ".ssa", ".vtt", ".sub" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly EnumerationOptions TopLevelFiles = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
    };

    // The name comes from the video and the language only: nothing a provider sent ends up in it but a checked extension.
    public static string NameFor(string videoPath, Language language, bool hearingImpaired, bool forced, string extension)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (!Extensions.Contains(extension))
        {
            throw new ArgumentException($"'{extension}' is not a subtitle extension.", nameof(extension));
        }

        string code = IsoLanguages.Get(language)?.TwoLetterCode ?? throw new ArgumentException($"{language} has no language code.", nameof(language));
        return $"{Path.GetFileNameWithoutExtension(videoPath)}.{code}{(hearingImpaired ? ".hi" : "")}{(forced ? ".forced" : "")}{extension.ToLowerInvariant()}";
    }

    // The subtitle a provider sent, or the single one inside the archive it sent (extracted with ArchiveExtractor's caps in a
    // scratch folder). Returns its extension and content, or the reason it is refused.
    public static (string Extension, byte[] Content)? Unpack(string fileName, byte[] content, PostProcessingOptions options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(content);
        string extension = Path.GetExtension(fileName);
        if (FileClassifier.Classify(fileName, options) != FileKind.Archive)
        {
            error = Check(extension, content.LongLength);
            return error is null ? (extension.ToLowerInvariant(), content) : null;
        }

        string scratch = Directory.CreateTempSubdirectory("modularr-subtitle-").FullName;
        try
        {
            string archive = Path.Combine(scratch, "subtitle" + extension.ToLowerInvariant());
            File.WriteAllBytes(archive, content);
            ArchiveExtraction extraction = ArchiveExtractor.ExtractAll([archive], options, Path.Combine(scratch, "files"));
            if (extraction.Error is not null)
            {
                error = extraction.Error;
                return null;
            }

            FileInfo? subtitle = extraction.ExtractedFiles.Select(f => new FileInfo(f))
                .FirstOrDefault(f => Check(f.Extension, f.Length) is null);
            error = subtitle is null ? "the archive holds no subtitle file Modularr accepts" : null;
            return subtitle is null ? null : (subtitle.Extension.ToLowerInvariant(), File.ReadAllBytes(subtitle.FullName));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static string? Check(string extension, long size) =>
        !Extensions.Contains(extension) ? $"'{extension}' is not an accepted subtitle type ({string.Join(", ", Extensions.Order(StringComparer.Ordinal))})"
        : size == 0 ? "the subtitle file is empty"
        : size > MaxBytes ? $"the subtitle file is larger than {MaxBytes / 1024 / 1024} MiB"
        : null;

    // Writes the subtitle next to the video. Null when that file already exists: an existing subtitle is never replaced.
    public static string? Save(string videoPath, IReadOnlyList<string> roots, Language language, bool hearingImpaired, bool forced, string extension, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        string target = Path.Combine(EnsureInLibrary(videoPath, roots), NameFor(videoPath, language, hearingImpaired, forced, extension));
        try
        {
            using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write);
            output.Write(content);
            return target;
        }
        catch (IOException) when (File.Exists(target))
        {
            return null;
        }
    }

    // The subtitles already next to the video, by the "<base>.<lang>[.tags]<ext>" convention.
    public static IReadOnlyList<SubtitleFile> Present(string videoPath, IReadOnlyList<string> roots)
    {
        string folder = EnsureInLibrary(videoPath, roots);
        return Directory.Exists(folder) ? Match(videoPath, Directory.EnumerateFiles(folder, "*", TopLevelFiles)) : [];
    }

    // Present for many videos, each folder checked and listed once. A video outside the roots, behind a link or unreadable has none.
    public static Dictionary<string, IReadOnlyList<SubtitleFile>> PresentAll(IEnumerable<string> videoPaths, IReadOnlyList<string> roots)
    {
        Dictionary<string, string[]> folders = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, IReadOnlyList<SubtitleFile>> present = new(StringComparer.Ordinal);
        foreach (string video in videoPaths)
        {
            string key = Path.GetDirectoryName(Path.GetFullPath(video)) ?? "";
            if (!folders.TryGetValue(key, out string[]? files))
            {
                try
                {
                    string folder = EnsureInLibrary(video, roots);
                    files = Directory.Exists(folder) ? [.. Directory.EnumerateFiles(folder, "*", TopLevelFiles).Where(f => Extensions.Contains(Path.GetExtension(f)))] : [];
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    files = [];
                }

                folders[key] = files;
            }

            present[video] = Match(video, files);
        }

        return present;
    }

    private static IReadOnlyList<SubtitleFile> Match(string videoPath, IEnumerable<string> files)
    {
        string prefix = Path.GetFileNameWithoutExtension(videoPath) + ".";
        return
        [
            .. files
                .Where(f => Extensions.Contains(Path.GetExtension(f)) && Path.GetFileNameWithoutExtension(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(f => Parse(f, Path.GetFileNameWithoutExtension(f)[prefix.Length..].Split('.')))
                .OfType<SubtitleFile>(),
        ];
    }

    private static SubtitleFile? Parse(string path, string[] tokens)
    {
        Language? language = IsoLanguages.Find(tokens[0])?.Language;
        string[] tags = [.. tokens.Skip(1).Select(t => t.ToLowerInvariant())];
        return language is null || language == Language.Unknown
            ? null
            : new SubtitleFile(path, language, tags.Any(t => t is "hi" or "sdh" or "cc"), tags.Contains("forced"));
    }

    // The video's folder, once it is known to be inside a library root and reached without a link on the way.
    private static string EnsureInLibrary(string videoPath, IReadOnlyList<string> roots)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(videoPath)) ?? throw new ArgumentException("The video has no folder.", nameof(videoPath));
        string? root = roots.Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r)))
            .FirstOrDefault(r => folder.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (root is null)
        {
            throw new ArgumentException($"'{folder}' is not inside a library folder.", nameof(videoPath));
        }

        for (DirectoryInfo? directory = new(folder); directory is not null && directory.FullName.Length > root.Length; directory = directory.Parent)
        {
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ArgumentException($"'{directory.FullName}' is a link, subtitles are not written through it.", nameof(videoPath));
            }
        }

        return folder;
    }

    // OpenSubtitles moviehash: the file size plus the 64-bit little-endian sums of its first and last 64 KiB. Null below 128 KiB.
    public static string? MovieHash(string videoPath)
    {
        const int Chunk = 64 * 1024;
        using FileStream file = new(videoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (file.Length < 2 * Chunk)
        {
            return null;
        }

        ulong hash = (ulong)file.Length;
        byte[] buffer = new byte[Chunk];
        foreach (long offset in new[] { 0, file.Length - Chunk })
        {
            file.Position = offset;
            file.ReadExactly(buffer);
            for (int i = 0; i < Chunk; i += sizeof(ulong))
            {
                hash = unchecked(hash + BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(i)));
            }
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}
