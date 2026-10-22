// RAR fixtures (Rar*.rar, Rar*.r0*) are from the SharpCompress test archives, MIT (SharpCompress-LICENSE.txt):
// https://github.com/adamhathcock/sharpcompress/tree/master/tests/TestArchives/Archives
// episode.tar.xz and Show.S01E01.mkv.xz were made with bsdtar (SharpCompress cannot write xz).
using System.IO.Compression;
using System.Security.Cryptography;
using Modularr.Core.PostProcessing;
using SharpCompress.Compressors.BZip2;

namespace Modularr.Core.Tests.PostProcessing;

public sealed class ArchiveExtractionTests : IDisposable
{
    private static readonly Func<Stream, Stream> GZip = stream => new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true);

    private static readonly Func<Stream, Stream> BZip2 = stream =>
        BZip2Stream.Create(stream, SharpCompress.Compressors.CompressionMode.Compress, decompressConcatenated: false, leaveOpen: true, tolerateTruncatedStream: false);

    private readonly TempDownload _download = new();

    public void Dispose() => _download.Dispose();

    private PostProcessingVerdict Process() => CompletedDownloadProcessor.Process(_download.Root, new PostProcessingOptions());

    private void AssertRejected(string reasonPart)
    {
        PostProcessingVerdict.Rejected rejected = Assert.IsType<PostProcessingVerdict.Rejected>(Process());
        Assert.Contains(reasonPart, rejected.Reason, StringComparison.Ordinal);
        Assert.True(rejected.Blacklist);
    }

    private void AssertAcceptedEpisode()
    {
        PostProcessingVerdict.Accepted accepted = Assert.IsType<PostProcessingVerdict.Accepted>(Process());
        Assert.Equal([_download.PathOf("Show.S01E01.mkv")], accepted.VideoFiles);
        Assert.Equal(["Show.S01E01.mkv"], _download.Files());
    }

    [Theory]
    [InlineData("Show.S01E01.tar")]
    [InlineData("Show.S01E01.tar.gz")]
    [InlineData("Show.S01E01.tgz")]
    [InlineData("Show.S01E01.tar.bz2")]
    [InlineData("Show.S01E01.tbz2")]
    [InlineData("Show.S01E01.tb2")]
    [InlineData("Show.S01E01.mkv.gz")]
    [InlineData("Show.S01E01.mkv.bz2")]
    public void ExtractsTarAndSingleFileCompression(string archive)
    {
        byte[] content = archive.Contains(".mkv.", StringComparison.Ordinal) ? TempDownload.Video : TempDownload.TarBytes(("Show.S01E01.mkv", TempDownload.Video));
        _download.Write(archive, Path.GetExtension(archive) switch
        {
            ".gz" or ".tgz" => TempDownload.Compress(content, GZip),
            ".bz2" or ".tbz2" or ".tb2" => TempDownload.Compress(content, BZip2),
            _ => content,
        });

        AssertAcceptedEpisode();
    }

    [Theory]
    [InlineData("episode.tar.xz")]
    [InlineData("Show.S01E01.mkv.xz")]
    public void ExtractsXz(string fixture)
    {
        _download.Fixture(fixture);

        AssertAcceptedEpisode();
    }

    [Theory]
    [InlineData("Rar4.rar", 1)]
    [InlineData("Rar5.rar", 1)]
    [InlineData("Rar5.multi.", 6)]
    [InlineData("Rar2.multi.", 7)]
    public void ExtractsRarFromItsFirstVolumeOnly(string fixtures, int volumes)
    {
        _download.Fixture(fixtures);

        ArchiveExtraction extraction = ArchiveExtractor.ExtractAll([.. Directory.EnumerateFiles(_download.Root)], new PostProcessingOptions());

        Assert.Null(extraction.Error);
        Assert.Equal(volumes, extraction.Volumes.Count);
        Assert.Equal(["exe/test.exe", "jpg/test.jpg", "тест.txt"], extraction.ExtractedFiles.Select(f => Path.GetRelativePath(_download.Root, f).Replace('\\', '/')).Order(StringComparer.Ordinal));
        Assert.Equal(45_056, new FileInfo(_download.PathOf(Path.Combine("exe", "test.exe"))).Length);
    }

    // The RAR fixtures hold an .exe: extracted, then refused by the post-extraction check.
    [Fact]
    public void RejectsWhatARarHolds()
    {
        _download.Fixture("Rar5.multi.");

        AssertRejected(".exe");
        Assert.DoesNotContain(_download.Files(), f => f.StartsWith("Rar5.multi.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Rar.encrypted_filesOnly.rar")]
    [InlineData("Rar5.comment.filesOnly.rar")]
    [InlineData("Rar5.solid.encrypted_headers.rar")]
    public void RejectsPasswordProtectedRar(string fixture)
    {
        _download.Fixture(fixture);

        AssertRejected("password protected");
        Assert.Equal([fixture], _download.Files());
    }

    [Fact]
    public void RejectsCorruptRar()
    {
        _download.Fixture("Rar.malformed_512byte.rar");

        AssertRejected("Rar.malformed_512byte.rar: corrupt archive");
        Assert.Equal(["Rar.malformed_512byte.rar"], _download.Files());
    }

    [Fact]
    public void RemovesAPartiallyWrittenFileOnFailure()
    {
        // Random content does not compress: the cut stream fails as corrupt, not as a bomb.
        byte[] tarGz = TempDownload.Compress(TempDownload.TarBytes(("Show.S01E01.mkv", RandomNumberGenerator.GetBytes(256 * 1024))), GZip);
        _download.Write("Show.S01E01.tar.gz", tarGz[..(tarGz.Length / 2)]);

        AssertRejected("Show.S01E01.tar.gz: corrupt archive");
        Assert.Equal(["Show.S01E01.tar.gz"], _download.Files());
    }

    [Fact]
    public void RemovesEveryLevelWhenANestedArchiveFails()
    {
        byte[] inner = TempDownload.ZipBytes(("Show.S01E01.mkv", TempDownload.Video));
        _download.Zip("dl/outer.zip", ("Extras/Show.S01E01.nfo", TempDownload.Video), ("Extras/inner.zip", inner[..(inner.Length / 2)]));

        AssertRejected("inner.zip");
        Assert.Equal(["dl/outer.zip"], _download.Files());
        Assert.Equal(["outer.zip"], Directory.EnumerateFileSystemEntries(_download.PathOf("dl")).Select(Path.GetFileName));
    }

    // Each inner archive is under the entry cap, the tree is not.
    [Fact]
    public void AppliesTheEntryCapOverTheWholeTree()
    {
        byte[] Inner(string prefix) => TempDownload.ZipBytes([.. Enumerable.Range(0, 5_001).Select(i => ($"{prefix}{i}.nfo", Array.Empty<byte>()))]);
        _download.Zip("outer.zip", ("a.zip", Inner("a")), ("b.zip", Inner("b")));

        AssertRejected("b.zip: archive too large");
        Assert.Equal(["outer.zip"], _download.Files());
    }

    [Fact]
    public void ExtractsACompressedTarInsideAZip()
    {
        byte[] tarGz = TempDownload.Compress(TempDownload.TarBytes(("Show.S01E01.mkv", TempDownload.Video)), GZip);
        _download.Zip("Show.S01E01.zip", ("Show.S01E01.tar.gz", tarGz));

        AssertAcceptedEpisode();
    }
}
