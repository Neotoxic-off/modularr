using Modularr.Core.PostProcessing;

namespace Modularr.Core.Tests.PostProcessing;

public sealed class CompletedDownloadProcessorTests : IDisposable
{
    private static readonly byte[] Payload = new byte[16];

    private readonly TempDownload _download = new();

    public void Dispose() => _download.Dispose();

    private PostProcessingVerdict Process(string? path = null) =>
        CompletedDownloadProcessor.Process(path ?? _download.Root, new PostProcessingOptions());

    private PostProcessingVerdict.Rejected AssertRejected(string reasonPart)
    {
        PostProcessingVerdict.Rejected rejected = Assert.IsType<PostProcessingVerdict.Rejected>(Process());
        Assert.Contains(reasonPart, rejected.Reason, StringComparison.Ordinal);
        Assert.True(rejected.Blacklist);
        return rejected;
    }

    [Fact]
    public void DeletesIgnorableFilesSilently()
    {
        _download.CreateFile("Show.S01E01/Show.S01E01.mkv");
        _download.CreateFile("Show.S01E01/Show.S01E01.nfo");
        _download.CreateFile("Show.S01E01/poster.jpg");
        _download.CreateFile("Show.S01E01/Show.S01E01.sfv");
        _download.CreateFile("Show.S01E01/Thumbs.db");

        Assert.IsType<PostProcessingVerdict.Accepted>(Process());
        Assert.Equal(["Show.S01E01/Show.S01E01.mkv"], _download.Files());
    }

    [Fact]
    public void DeletesDisallowedFileAndRejects()
    {
        _download.CreateFile("Show.S01E01.mkv");
        _download.CreateFile("Bonus.mp3");

        AssertRejected(".mp3");
        Assert.Equal(["Show.S01E01.mkv"], _download.Files());
    }

    [Fact]
    public void DeletesSampleAndKeepsMainVideo()
    {
        _download.CreateFile("Show.S01E01.mkv");
        _download.CreateFile("Sample/Show.S01E01.sample.mkv");

        PostProcessingVerdict.Accepted accepted = Assert.IsType<PostProcessingVerdict.Accepted>(Process());

        Assert.Equal([_download.PathOf("Show.S01E01.mkv")], accepted.VideoFiles);
        Assert.Equal(["Show.S01E01.mkv"], _download.Files());
    }

    [Fact]
    public void RejectsWhenOnlySamplesRemain()
    {
        _download.CreateFile("Show.S01E01.sample.mkv");
        _download.CreateFile("Show.S01E01.nfo");

        AssertRejected("No video file found");
        Assert.Empty(_download.Files());
    }

    [Fact]
    public void RejectsMissingPathWithoutBlacklisting()
    {
        PostProcessingVerdict.Rejected rejected = Assert.IsType<PostProcessingVerdict.Rejected>(Process(_download.PathOf("nope")));

        Assert.False(rejected.Blacklist);
    }

    [Fact]
    public void ProcessesASingleVideoFile()
    {
        string video = _download.CreateFile("Movie (2000).mkv");
        _download.CreateFile("Other.Download.exe");

        PostProcessingVerdict.Accepted accepted = Assert.IsType<PostProcessingVerdict.Accepted>(Process(video));

        Assert.Equal([video], accepted.VideoFiles);
        Assert.Equal(["Movie (2000).mkv", "Other.Download.exe"], _download.Files());
    }

    [Fact]
    public void ExtractsASingleArchiveFileNextToIt()
    {
        string archive = _download.Zip("Movie (2000).zip", ("Movie (2000)/Movie (2000).mkv", Payload), ("Movie (2000)/Movie (2000).en.srt", Payload));

        PostProcessingVerdict.Accepted accepted = Assert.IsType<PostProcessingVerdict.Accepted>(Process(archive));

        Assert.Equal([_download.PathOf(Path.Combine("Movie (2000)", "Movie (2000).mkv"))], accepted.VideoFiles);
        Assert.Equal([_download.PathOf(Path.Combine("Movie (2000)", "Movie (2000).en.srt"))], accepted.SubtitleFiles);
        Assert.Equal(["Movie (2000)/Movie (2000).en.srt", "Movie (2000)/Movie (2000).mkv"], _download.Files());
    }

    [Fact]
    public void ExtractsArchiveInArchiveOneLevel()
    {
        byte[] inner = TempDownload.ZipBytes(("Show.S01E01.mkv", Payload), ("Show.S01E01.nfo", Payload));
        _download.Zip("Show.S01E01/outer.zip", ("inner.zip", inner));

        PostProcessingVerdict.Accepted accepted = Assert.IsType<PostProcessingVerdict.Accepted>(Process());

        Assert.Equal([_download.PathOf(Path.Combine("Show.S01E01", "Show.S01E01.mkv"))], accepted.VideoFiles);
        Assert.Equal(["Show.S01E01/Show.S01E01.mkv"], _download.Files());
    }

    [Fact]
    public void RejectsArchiveNestedTwoLevels()
    {
        byte[] innermost = TempDownload.ZipBytes(("Show.S01E01.mkv", Payload));
        byte[] inner = TempDownload.ZipBytes(("innermost.zip", innermost));
        _download.Zip("outer.zip", ("inner.zip", inner));

        AssertRejected(".zip");
        Assert.Empty(_download.Files());
    }

    [Fact]
    public void RejectsExecutableHiddenInArchive()
    {
        _download.Zip("Show.S01E01.zip", ("Show.S01E01.mkv", Payload), ("Codec.Installer.exe", Payload));

        AssertRejected(".exe");
        Assert.Equal(["Show.S01E01.mkv"], _download.Files());
    }

    [Theory]
    [InlineData("../evil.mkv")]
    [InlineData("sub/../../evil.mkv")]
    public void RefusesZipSlipWithoutWritingAnything(string entryName)
    {
        _download.Zip("dl/Show.S01E01.zip", ("Show.S01E01.mkv", Payload), (entryName, Payload));

        PostProcessingVerdict.Rejected rejected = Assert.IsType<PostProcessingVerdict.Rejected>(Process(_download.PathOf("dl")));

        Assert.True(rejected.Blacklist);
        Assert.Contains("escapes the download folder", rejected.Reason, StringComparison.Ordinal);
        Assert.Equal(["dl/Show.S01E01.zip"], _download.Files());
    }

    [Fact]
    public void RejectsPasswordProtectedZip()
    {
        File.WriteAllBytes(_download.PathOf("Show.S01E01.zip"), TempDownload.MarkEncrypted(TempDownload.ZipBytes(("Show.S01E01.mkv", Payload))));

        AssertRejected("password protected");
    }

    [Fact]
    public void RejectsCorruptArchive()
    {
        byte[] zip = TempDownload.ZipBytes(("Show.S01E01.mkv", Payload));
        File.WriteAllBytes(_download.PathOf("Show.S01E01.zip"), zip[..(zip.Length / 2)]);

        AssertRejected("Show.S01E01.zip");
    }

    [Fact]
    public void Extracts7z()
    {
        _download.Fixture("episode.7z");

        PostProcessingVerdict.Accepted accepted = Assert.IsType<PostProcessingVerdict.Accepted>(Process());

        Assert.Equal([_download.PathOf("Show.S01E01.mkv")], accepted.VideoFiles);
        Assert.Equal([_download.PathOf("Show.S01E01.srt")], accepted.SubtitleFiles);
        Assert.Equal("fake matroska payload\n", File.ReadAllText(_download.PathOf("Show.S01E01.mkv")));
        Assert.Equal(["Show.S01E01.mkv", "Show.S01E01.srt"], _download.Files());
    }

    [Theory]
    [InlineData("encrypted.7z")]
    [InlineData("encrypted-headers.7z")]
    public void RejectsPasswordProtected7z(string fixture)
    {
        _download.Fixture(fixture);

        AssertRejected("password protected");
        Assert.Equal([fixture], _download.Files());
    }

    // shortcut: no rar fixture (no free rar creator), multi-volume rar is covered by volume naming only, add real fixtures when one can be produced legitimately.
    [Theory]
    [InlineData("Show.S01E01.rar", true)]
    [InlineData("Show.S01E01.r00", false)]
    [InlineData("Show.S01E01.r01", false)]
    [InlineData("Show.S01E01.r100", false)]
    [InlineData("Show.S01E01.part1.rar", true)]
    [InlineData("Show.S01E01.part01.rar", true)]
    [InlineData("Show.S01E01.part001.rar", true)]
    [InlineData("Show.S01E01.part02.rar", false)]
    [InlineData("Show.S01E01.part10.rar", false)]
    [InlineData("Show.S01E01.zip", true)]
    [InlineData("Show.S01E01.7z", true)]
    public void OpensMultiVolumeArchivesFromTheirFirstVolume(string fileName, bool expected)
    {
        Assert.Equal(expected, ArchiveExtractor.IsFirstVolume(fileName));
    }
}
