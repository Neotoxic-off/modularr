namespace Modularr.Core.PostProcessing;

public static class CompletedDownloadProcessor
{
    // Archive in archive is extracted, one level only.
    private const int ExtractionPasses = 2;

    // Links are never followed: one pointing outside the download would get its target's files deleted.
    private static readonly EnumerationOptions WalkOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    // Extracts archives in place, deletes everything that is not a kept video or subtitle, and judges the release.
    public static PostProcessingVerdict Process(string path, PostProcessingOptions options)
    {
        // Full paths, so files match the volumes and entries reported by the extractor.
        string fullPath = Path.GetFullPath(path);
        List<string> files;
        string root;

        if (Directory.Exists(fullPath))
        {
            if (new DirectoryInfo(fullPath).LinkTarget is not null)
            {
                return new PostProcessingVerdict.Rejected($"Download path is a link, not processed: {path}", Blacklist: false);
            }

            files = [.. Directory.EnumerateFiles(fullPath, "*", WalkOptions)];
            root = fullPath;
        }
        else if (File.Exists(fullPath))
        {
            files = [fullPath];
            root = Path.GetDirectoryName(fullPath)!;
        }
        else
        {
            return new PostProcessingVerdict.Rejected($"Download path not found: {path}", Blacklist: false);
        }

        root = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;

        void Delete(string file)
        {
            if (Path.GetFullPath(file).StartsWith(root, StringComparison.Ordinal))
            {
                File.Delete(file);
            }
        }

        List<string> pending = files;

        for (int pass = 0; pass < ExtractionPasses; pass++)
        {
            List<string> extracted = [];

            foreach (string archive in pending.Where(f => FileClassifier.Classify(f, options) == FileKind.Archive && ArchiveExtractor.IsFirstVolume(f)).ToList())
            {
                ArchiveExtraction extraction = ArchiveExtractor.Extract(archive);

                if (extraction.Error is not null)
                {
                    return new PostProcessingVerdict.Rejected(extraction.Error, Blacklist: true);
                }

                foreach (string volume in extraction.Volumes)
                {
                    Delete(volume);
                }

                files.RemoveAll(f => extraction.Volumes.Contains(f, StringComparer.OrdinalIgnoreCase));
                files.AddRange(extraction.ExtractedFiles.Except(files, StringComparer.OrdinalIgnoreCase));
                extracted.AddRange(extraction.ExtractedFiles);
            }

            pending = extracted;
        }

        List<string> videos = [];
        List<string> subtitles = [];
        List<string> dangerous = [];
        List<string> executables = [];
        List<string> disallowed = [];

        foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            FileKind kind = FileClassifier.Classify(file, options);

            List<string>? keep = kind switch
            {
                FileKind.Video when !SampleDetector.IsSample(file, new FileInfo(file).Length) => videos,
                FileKind.Subtitle => subtitles,
                FileKind.Dangerous => dangerous,
                FileKind.Executable => executables,
                FileKind.Archive or FileKind.Disallowed => disallowed,
                _ => null,
            };

            keep?.Add(file);

            if (keep != videos && keep != subtitles)
            {
                Delete(file);
            }
        }

        return (dangerous, executables, disallowed, videos) switch
        {
            ({ Count: > 0 }, _, _, _) => Reject($"Caution: Found potentially dangerous file with extension: {Extensions(dangerous)}"),
            (_, { Count: > 0 }, _, _) => Reject($"Caution: Found executable file with extension: {Extensions(executables)}"),
            (_, _, { Count: > 0 }, _) => Reject($"Found file with disallowed extension: {Extensions(disallowed)}"),
            (_, _, _, { Count: 0 }) => Reject("No video file found"),
            _ => new PostProcessingVerdict.Accepted(videos, subtitles),
        };
    }

    private static PostProcessingVerdict.Rejected Reject(string reason) => new(reason, Blacklist: true);

    private static string Extensions(List<string> files) =>
        string.Join(", ", files.Select(f => Path.GetExtension(f) is { Length: > 0 } extension ? extension : Path.GetFileName(f)).Distinct(StringComparer.OrdinalIgnoreCase));
}
