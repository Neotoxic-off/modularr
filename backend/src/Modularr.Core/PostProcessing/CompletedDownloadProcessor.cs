namespace Modularr.Core.PostProcessing;

public static class CompletedDownloadProcessor
{
    // Archive in archive is extracted, one level only.
    private const int ExtractionPasses = 2;

    // Extracts archives in place, deletes everything that is not a kept video or subtitle, and judges the release.
    public static PostProcessingVerdict Process(string path, PostProcessingOptions options)
    {
        // Full paths, so files match the volumes and entries reported by the extractor.
        string fullPath = Path.GetFullPath(path);
        List<string> files;

        if (Directory.Exists(fullPath))
        {
            files = [.. Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)];
        }
        else if (File.Exists(fullPath))
        {
            files = [fullPath];
        }
        else
        {
            return new PostProcessingVerdict.Rejected($"Download path not found: {path}", Blacklist: false);
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
                    File.Delete(volume);
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
                File.Delete(file);
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
