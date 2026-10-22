namespace Modularr.Core.PostProcessing;

public static class CompletedDownloadProcessor
{
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

        // A single-file download sits in the shared downloads folder: its archive gets a folder of its own,
        // so an entry like OtherRelease/x.mkv cannot land in another download's folder.
        string? destination = null;

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
            destination = Path.Combine(root, Path.GetFileNameWithoutExtension(fullPath));
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

        ArchiveExtraction extraction = ArchiveExtractor.ExtractAll(files, options, destination);

        if (extraction.Error is not null)
        {
            return Reject(extraction.Error);
        }

        foreach (string volume in extraction.Volumes)
        {
            Delete(volume);
        }

        files.RemoveAll(f => extraction.Volumes.Contains(f, StringComparer.OrdinalIgnoreCase));
        files.AddRange(extraction.ExtractedFiles.Except(files, StringComparer.OrdinalIgnoreCase));

        List<string> videos = [];
        List<string> subtitles = [];
        List<string> dangerous = [];
        List<string> executables = [];
        List<string> fakeVideos = [];
        List<string> disallowed = [];

        foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            FileKind kind = FileClassifier.Classify(file, options);

            List<string>? keep = kind switch
            {
                FileKind.Video when !SampleDetector.IsSample(file, new FileInfo(file).Length) => VideoSignature.IsVideoFile(file) ? videos : fakeVideos,
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

        if (RejectionReason(dangerous, executables, fakeVideos, disallowed, hasVideo: videos.Count > 0) is not string reason)
        {
            return new PostProcessingVerdict.Accepted(videos, subtitles);
        }

        // Removing the torrent with its data only deletes the torrent's own files, not what was extracted from them.
        foreach (string file in extraction.ExtractedFiles)
        {
            Delete(file);
        }

        return Reject(reason);
    }

    // Shared with the pre-download content check, so a release is rejected with the same reason before and after download.
    // Fake videos are only known after download: the pre-download check passes none.
    public static string? RejectionReason(
        IReadOnlyList<string> dangerous, IReadOnlyList<string> executables, IReadOnlyList<string> fakeVideos, IReadOnlyList<string> disallowed, bool hasVideo) =>
        (dangerous, executables, fakeVideos, disallowed, hasVideo) switch
        {
            ({ Count: > 0 }, _, _, _, _) => $"Caution: Found potentially dangerous file with extension: {Extensions(dangerous)}",
            (_, { Count: > 0 }, _, _, _) => $"Caution: Found executable file with extension: {Extensions(executables)}",
            (_, _, { Count: > 0 }, _, _) => $"Caution: Found file whose content is not a video: {string.Join(", ", fakeVideos.Select(Path.GetFileName))}",
            (_, _, _, { Count: > 0 }, _) => $"Found file with disallowed extension: {Extensions(disallowed)}",
            (_, _, _, _, false) => "No video file found",
            _ => null,
        };

    private static PostProcessingVerdict.Rejected Reject(string reason) => new(reason, Blacklist: true);

    private static string Extensions(IReadOnlyList<string> files) =>
        string.Join(", ", files.Select(f => Path.GetExtension(f) is { Length: > 0 } extension ? extension : Path.GetFileName(f)).Distinct(StringComparer.OrdinalIgnoreCase));
}
