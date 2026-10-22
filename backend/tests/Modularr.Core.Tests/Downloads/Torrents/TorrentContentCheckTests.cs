using Modularr.Core.Downloads.Torrents;
using Modularr.Core.PostProcessing;

namespace Modularr.Core.Tests.Downloads.Torrents;

public sealed class TorrentContentCheckTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    [Theory]
    [InlineData("Movie.2020.1080p.WEB-DL.mkv.exe", "executable")]
    [InlineData("Movie.2020.1080p.WEB-DL.scr", "dangerous")]
    [InlineData("Movie.2020.1080p.WEB-DL.lnk", "dangerous")]
    [InlineData("Codec/install.bat", "executable")]
    [InlineData("Codec/install.cmd", "executable")]
    [InlineData("Play.vbs", "dangerous")]
    [InlineData("Play.ps1", "dangerous")]
    [InlineData("Setup.msi", "disallowed")]
    [InlineData("Player.js", "disallowed")]
    [InlineData("Player.apk", "disallowed")]
    [InlineData("codec.dll", "disallowed")]
    [InlineData("password.zipx", "dangerous")]
    [InlineData("README", "disallowed")]
    public void RejectsAVideoShippedWithMalware(string extra, string reason)
    {
        TorrentContentVerdict verdict = Check(("Movie.2020.1080p.WEB-DL/Movie.2020.1080p.WEB-DL.mkv", Gigabyte), ($"Movie.2020.1080p.WEB-DL/{extra}", 1024));

        Assert.NotNull(verdict.Rejection);
        Assert.Contains(reason, verdict.Rejection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DangerousWinsOverExecutableInTheReason()
    {
        TorrentContentVerdict verdict = Check(("Movie.mkv", Gigabyte), ("run.exe", 1), ("Movie.lnk", 1));

        Assert.StartsWith("Caution: Found potentially dangerous file with extension: .lnk", verdict.Rejection, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsVideoWithSubtitlesPosterAndNfoButSkipsTheExtras()
    {
        TorrentContentVerdict verdict = Check(
            ("Show.S01E01.720p/Show.S01E01.720p.mkv", Gigabyte),
            ("Show.S01E01.720p/Show.S01E01.720p.en.srt", 50_000),
            ("Show.S01E01.720p/Subs/French.ass", 50_000),
            ("Show.S01E01.720p/poster.jpg", 200_000),
            ("Show.S01E01.720p/Screens/splash.png", 200_000),
            ("Show.S01E01.720p/release.nfo", 2_000));

        Assert.Null(verdict.Rejection);
        Assert.Equal([true, true, true, false, false, false], verdict.Wanted);
    }

    [Fact]
    public void SkipsSamplesTextAndWebsiteLinks()
    {
        TorrentContentVerdict verdict = Check(
            ("Movie.2020.1080p/Movie.2020.1080p.mkv", 4 * Gigabyte),
            ("Movie.2020.1080p/Sample/movie.2020.1080p.mkv", 30_000_000),
            ("Movie.2020.1080p/movie-sample.mkv", 30_000_000),
            ("Movie.2020.1080p/RARBG.txt", 30),
            ("Movie.2020.1080p/Visit the site.url", 100),
            ("Movie.2020.1080p/www.site.example.html", 100));

        Assert.Null(verdict.Rejection);
        Assert.Equal([true, false, false, false, false, false], verdict.Wanted);
    }

    [Fact]
    public void KeepsALargeVideoNamedSample()
    {
        TorrentContentVerdict verdict = Check(("Sample.Show.S01E01.mkv", Gigabyte));

        Assert.Null(verdict.Rejection);
        Assert.Equal([true], verdict.Wanted);
    }

    [Fact]
    public void AcceptsArchivesOnly()
    {
        TorrentContentVerdict verdict = Check(
            ("Movie.2020.1080p/movie.rar", 100_000_000),
            ("Movie.2020.1080p/movie.r00", 100_000_000),
            ("Movie.2020.1080p/movie.r01", 100_000_000),
            ("Movie.2020.1080p/movie.sfv", 300),
            ("Movie.2020.1080p/movie.nfo", 3000));

        Assert.Null(verdict.Rejection);
        Assert.Equal([true, true, true, false, false], verdict.Wanted);
    }

    [Theory]
    [InlineData("movie.zip")]
    [InlineData("movie.7z")]
    [InlineData("movie.tar")]
    [InlineData("movie.tgz")]
    [InlineData("movie.part01.rar")]
    public void AcceptsEveryArchiveFormat(string archive)
    {
        Assert.Null(Check((archive, Gigabyte)).Rejection);
    }

    [Fact]
    public void RejectsWithoutAnyVideo()
    {
        TorrentContentVerdict verdict = Check(("poster.jpg", 200_000), ("release.nfo", 2_000), ("Show.en.srt", 50_000));

        Assert.Equal("No video file found", verdict.Rejection);
    }

    [Fact]
    public void RejectsWhenTheOnlyVideoIsASample()
    {
        Assert.Equal("No video file found", Check(("Movie/Sample/movie.mkv", 20_000_000), ("Movie/movie.nfo", 1_000)).Rejection);
    }

    [Fact]
    public void RejectsAnEmptyFileList()
    {
        Assert.Equal("No video file found", Check().Rejection);
    }

    // Disc images are a video format of the post-processing lists; removing them there rejects them before download too.
    [Fact]
    public void FollowsThePostProcessingVideoList()
    {
        Assert.Null(Check(("Movie.2020.BluRay.iso", 40 * Gigabyte)).Rejection);

        PostProcessingOptions withoutIso = new()
        {
            VideoExtensions = new PostProcessingOptions().VideoExtensions.Where(e => e != ".iso").ToHashSet(StringComparer.OrdinalIgnoreCase),
        };
        Assert.Contains(".iso", TorrentContentCheck.Check([("Movie.2020.BluRay.iso", 40 * Gigabyte)], withoutIso).Rejection, StringComparison.Ordinal);
    }

    private static TorrentContentVerdict Check(params (string Path, long Size)[] files) => TorrentContentCheck.Check(files, new PostProcessingOptions());
}
