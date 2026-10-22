// Ported from Sonarr RejectedFileExtensionServiceFixture, Sonarr DownloadedEpisodesImportServiceFixture and Radarr DownloadedMoviesImportServiceFixture (executable/archive cases).
using Modularr.Core.PostProcessing;

namespace Modularr.Core.Tests.PostProcessing;

public sealed class RejectedFileExtensionTests : IDisposable
{
    private readonly TempDownload _download = new();

    public void Dispose() => _download.Dispose();

    private PostProcessingVerdict Process(PostProcessingOptions? options = null) =>
        CompletedDownloadProcessor.Process(_download.Root, options ?? new PostProcessingOptions());

    [Fact]
    public void DoesNotRejectMediaFiles()
    {
        _download.CreateFile("Droned.S01E01.mkv");
        _download.CreateFile("Droned.S01E01.srt");

        PostProcessingVerdict.Accepted accepted = Assert.IsType<PostProcessingVerdict.Accepted>(Process());

        Assert.Equal([_download.PathOf("Droned.S01E01.mkv")], accepted.VideoFiles);
        Assert.Equal([_download.PathOf("Droned.S01E01.srt")], accepted.SubtitleFiles);
    }

    // Sonarr expected no rejection for a file without an extension: modularr removes and blacklists anything not in an allowed format.
    [Fact]
    public void RejectsFilesWithoutAnExtension()
    {
        _download.CreateFile("Droned.S01E01.mkv");
        _download.CreateFile("Droned.S01E01");

        PostProcessingVerdict.Rejected rejected = Assert.IsType<PostProcessingVerdict.Rejected>(Process());

        Assert.True(rejected.Blacklist);
        Assert.Contains(".S01E01", rejected.Reason, StringComparison.Ordinal);
        Assert.Equal(["Droned.S01E01.mkv"], _download.Files());
    }

    [Fact]
    public void RejectsDangerousFile()
    {
        _download.CreateFile("Droned.S01E01.mkv");
        _download.CreateFile("Droned.S01E01.lnk");

        PostProcessingVerdict.Rejected rejected = Assert.IsType<PostProcessingVerdict.Rejected>(Process());

        Assert.True(rejected.Blacklist);
        Assert.Contains(".lnk", rejected.Reason, StringComparison.Ordinal);
        Assert.Contains("dangerous", rejected.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Droned.S01E01.lnk", _download.Files());
    }

    [Fact]
    public void RejectsExecutableFile()
    {
        _download.CreateFile("Droned.S01E01/Droned.S01E01.bat");

        PostProcessingVerdict.Rejected rejected = Assert.IsType<PostProcessingVerdict.Rejected>(Process());

        Assert.True(rejected.Blacklist);
        Assert.Contains(".bat", rejected.Reason, StringComparison.Ordinal);
        Assert.Contains("executable", rejected.Reason, StringComparison.Ordinal);
        Assert.Empty(_download.Files());
    }

    // Upstream rejects through a "user rejected extensions" string; here the allowed video list is overridden instead.
    [Fact]
    public void RejectsExtensionRemovedFromTheAllowedList()
    {
        _download.CreateFile("Droned.S01E01.ISO");
        PostProcessingOptions options = new()
        {
            VideoExtensions = new PostProcessingOptions().VideoExtensions.Where(e => e != ".iso").ToHashSet(StringComparer.OrdinalIgnoreCase),
        };

        PostProcessingVerdict.Rejected rejected = Assert.IsType<PostProcessingVerdict.Rejected>(Process(options));

        Assert.Contains(".ISO", rejected.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsIsoWithDefaultOptions()
    {
        _download.CreateFile("Droned.S01E01.iso");

        Assert.IsType<PostProcessingVerdict.Accepted>(Process());
    }

    [Fact]
    public void RejectsWhenOnlyAnExecutableIsPresent()
    {
        _download.CreateFile("[HorribleSubs] American Psycho (2000) [720p]/[HorribleSubs] American Psycho (2000) [720p].exe");

        PostProcessingVerdict.Rejected rejected = Assert.IsType<PostProcessingVerdict.Rejected>(Process());

        Assert.Contains(".exe", rejected.Reason, StringComparison.Ordinal);
    }

    // Upstream rejected a folder holding a rar ("might need to be extracted"): modularr extracts it instead.
    [Fact]
    public void ExtractsArchiveInsteadOfRejectingIt()
    {
        _download.Zip("Droned.S01E01.zip", ("Droned.S01E01.mkv", TempDownload.Video));

        PostProcessingVerdict.Accepted accepted = Assert.IsType<PostProcessingVerdict.Accepted>(Process());

        Assert.Equal([_download.PathOf("Droned.S01E01.mkv")], accepted.VideoFiles);
        Assert.Equal(["Droned.S01E01.mkv"], _download.Files());
    }

    [Theory]
    [InlineData("Show.S01E01.mkv", FileKind.Video)]
    [InlineData("Show.S01E01.MP4", FileKind.Video)]
    [InlineData("Show.S01E01.m2ts", FileKind.Video)]
    [InlineData("Show.S01E01.srt", FileKind.Subtitle)]
    [InlineData("Show.S01E01.en.ass", FileKind.Subtitle)]
    [InlineData("Show.S01E01.sup", FileKind.Subtitle)]
    [InlineData("Show.S01E01.idx", FileKind.Subtitle)]
    [InlineData("Show.S01E01.vtt", FileKind.Subtitle)]
    [InlineData("Show.S01E01.nfo", FileKind.Ignorable)]
    [InlineData("Show.S01E01.txt", FileKind.Ignorable)]
    [InlineData("poster.jpg", FileKind.Ignorable)]
    [InlineData("Show.S01E01.sfv", FileKind.Ignorable)]
    [InlineData("Thumbs.db", FileKind.Ignorable)]
    [InlineData(".DS_Store", FileKind.Ignorable)]
    [InlineData("Show.S01E01.rar", FileKind.Archive)]
    [InlineData("Show.S01E01.r00", FileKind.Archive)]
    [InlineData("Show.S01E01.r17", FileKind.Archive)]
    [InlineData("Show.S01E01.part02.rar", FileKind.Archive)]
    [InlineData("Show.S01E01.zip", FileKind.Archive)]
    [InlineData("Show.S01E01.7z", FileKind.Archive)]
    [InlineData("Show.S01E01.tgz", FileKind.Archive)]
    [InlineData("Show.S01E01.arj", FileKind.Dangerous)]
    [InlineData("Show.S01E01.lnk", FileKind.Dangerous)]
    [InlineData("Show.S01E01.lzh", FileKind.Dangerous)]
    [InlineData("Show.S01E01.ps1", FileKind.Dangerous)]
    [InlineData("Show.S01E01.scr", FileKind.Dangerous)]
    [InlineData("Show.S01E01.vbs", FileKind.Dangerous)]
    [InlineData("Show.S01E01.zipx", FileKind.Dangerous)]
    [InlineData("Show.S01E01.bat", FileKind.Executable)]
    [InlineData("Show.S01E01.cmd", FileKind.Executable)]
    [InlineData("Show.S01E01.EXE", FileKind.Executable)]
    [InlineData("Show.S01E01.sh", FileKind.Executable)]
    [InlineData("Show.S01E01.mp3", FileKind.Disallowed)]
    [InlineData("Show.S01E01.url", FileKind.Ignorable)]
    [InlineData("file_id.diz", FileKind.Ignorable)]
    [InlineData("Show.S01E01.htm", FileKind.Ignorable)]
    [InlineData("Show.S01E01.html", FileKind.Ignorable)]
    [InlineData("Show.S01E01.log", FileKind.Ignorable)]
    [InlineData("Show.S01E01.xml", FileKind.Ignorable)]
    [InlineData("Show.S01E01.mka", FileKind.Ignorable)]
    [InlineData("Show.S01E01.ac3", FileKind.Ignorable)]
    [InlineData("Show.S01E01.dts", FileKind.Ignorable)]
    [InlineData("Show.S01E01.flac", FileKind.Ignorable)]
    [InlineData("Show.S01E01", FileKind.Disallowed)]
    [InlineData("README", FileKind.Disallowed)]
    public void ClassifiesByExtension(string fileName, FileKind expected)
    {
        Assert.Equal(expected, FileClassifier.Classify(fileName, new PostProcessingOptions()));
    }

    [Fact]
    public void ExecutableStaysExecutableEvenIfConfiguredAsVideo()
    {
        PostProcessingOptions options = new() { VideoExtensions = new HashSet<string>([".exe"], StringComparer.OrdinalIgnoreCase) };

        Assert.Equal(FileKind.Executable, FileClassifier.Classify("Show.S01E01.exe", options));
    }
}
