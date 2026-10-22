using Modularr.Core.PostProcessing;

namespace Modularr.Core.Downloads.Torrents;

// Wanted[i] tells whether file i of the torrent is downloaded; meaningless when Rejection is set.
public sealed record TorrentContentVerdict(string? Rejection, IReadOnlyList<bool> Wanted);

// Judges a torrent from its file list before any payload is downloaded, with the post-processing rules: an executable or an
// unknown file rejects the release; videos, subtitles and archives (re-checked once extracted) are downloaded; samples and
// ignorable extras (.nfo, images, .txt, .url...) are allowed but skipped. Disc images (.iso/.img) count as video like in
// post-processing: remove them from postProcessing.videoExtensions to reject them in both checks.
public static class TorrentContentCheck
{
    public static TorrentContentVerdict Check(IReadOnlyList<(string Path, long Size)> files, PostProcessingOptions options)
    {
        ArgumentNullException.ThrowIfNull(files);
        FileKind[] kinds = [.. files.Select(f => FileClassifier.Classify(f.Path, options))];
        bool[] wanted =
        [
            .. files.Select((file, i) => kinds[i] switch
            {
                FileKind.Video => !SampleDetector.IsSample(file.Path, file.Size),
                FileKind.Subtitle or FileKind.Archive => true,
                _ => false,
            }),
        ];

        string[] Of(FileKind kind) => [.. files.Where((_, i) => kinds[i] == kind).Select(f => f.Path)];
        bool hasPayload = Enumerable.Range(0, files.Count).Any(i => wanted[i] && kinds[i] is FileKind.Video or FileKind.Archive);

        return new TorrentContentVerdict(
            CompletedDownloadProcessor.RejectionReason(Of(FileKind.Dangerous), Of(FileKind.Executable), Of(FileKind.Disallowed), hasPayload),
            wanted);
    }
}
