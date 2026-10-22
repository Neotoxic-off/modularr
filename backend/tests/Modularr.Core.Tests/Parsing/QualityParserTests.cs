using Modularr.Core.Parsing;
using Modularr.Core.Qualities;

namespace Modularr.Core.Tests.Parsing;

public sealed class QualityParserTests
{
    public static TheoryData<string> QualityNames => [.. Quality.All.Where(q => q != Quality.Unknown).Select(q => q.Name)];

    [Theory]
    [MemberData(nameof(QualityNames))]
    public void ParsesItsOwnQualityName(string name)
    {
        Assert.Equal(name, QualityParser.ParseQuality($"My series S01E01 [{name}]").Quality.Name);
    }

    [Theory]
    [InlineData("SD TV", "SDTV")]
    [InlineData("SD DVD", "DVD")]
    [InlineData("480p WEB-DL", "WEBDL-480p")]
    [InlineData("HD TV", "HDTV-720p")]
    [InlineData("1080p HD TV", "HDTV-1080p")]
    [InlineData("2160p HD TV", "HDTV-2160p")]
    [InlineData("720p WEB-DL", "WEBDL-720p")]
    [InlineData("1080p WEB-DL", "WEBDL-1080p")]
    [InlineData("2160p WEB-DL", "WEBDL-2160p")]
    [InlineData("720p BluRay", "Bluray-720p")]
    [InlineData("1080p BluRay", "Bluray-1080p")]
    [InlineData("2160p BluRay", "Bluray-2160p")]
    [InlineData("1080p Remux", "Remux-1080p")]
    [InlineData("2160p Remux", "Remux-2160p")]
    public void ParsesQualityWhateverTheSeparator(string quality, string expected)
    {
        foreach (char separator in "-. _")
        {
            Assert.Equal(expected, QualityParser.ParseQuality($"My series S01E01 {quality.Replace(' ', separator)}").Quality.Name);
        }
    }

    [Theory]
    [InlineData("Series - S01E01 - Title [HDTV-1080p]")]
    [InlineData("Series.Title.S10E09.Episode.Title.1080i.UPSCALE.HDTV.DD5.1.MPEG2-zebra")]
    [InlineData("Series.Title.S01E01.Bluray720p")]
    [InlineData("Series.Title.S01E01.Bluray1080p")]
    [InlineData("Series.Title.S01E01.Bluray2160p")]
    [InlineData("Series.Title.S01E01.848x480.dvd")]
    [InlineData("Series.Title.S01E01.848x480.Bluray")]
    [InlineData("Series.Title.S01E01.1280x720.Bluray")]
    [InlineData("Series.Title.S01E01.1920x1080.Bluray")]
    [InlineData("Movie - 2018 [HDTV-1080p]")]
    public void DetectsSourceAndResolutionFromName(string title)
    {
        AssertDetection(title, QualityDetectionSource.Name, QualityDetectionSource.Name);
    }

    [Theory]
    [InlineData("[CR] Series Title - 004 [480p][48CE2D0F]")]
    [InlineData("Series.Title.S01E01.848x480")]
    [InlineData("Series.Title.S01E01.1280x720")]
    [InlineData("Series.Title.S01E01.1920x1080")]
    public void DetectsResolutionOnlyFromName(string title)
    {
        AssertDetection(title, QualityDetectionSource.Unknown, QualityDetectionSource.Name);
    }

    [Theory]
    [InlineData("Series.Title.2011.S02E01.WS.PDTV.x264-REPACK-TLA")]
    [InlineData("Series.Title.S01E01.Bluray")]
    [InlineData("Series.Title.S01E01.HD.TV")]
    [InlineData("Series.Title.S01E01.SD.TV")]
    public void DetectsSourceOnlyFromName(string title)
    {
        AssertDetection(title, QualityDetectionSource.Name, QualityDetectionSource.Unknown);
    }

    [Theory]
    [InlineData("Series.Title.S01E02.Chained.Heat.mkv")]
    [InlineData("Series - S01E01 - Title.avi")]
    [InlineData("Series.Title.9x18.sunshine_days.avi")]
    [InlineData("[CR] Series Title - 004 [48CE2D0F].avi")]
    public void DetectsQualityFromExtension(string title)
    {
        AssertDetection(title, QualityDetectionSource.Extension, QualityDetectionSource.Extension);
    }

    [Theory]
    [InlineData("Series.Title.S01E02.Chained.Heat.1080p.mkv")]
    [InlineData("Series - S01E01 - Title.720p.avi")]
    public void DetectsResolutionFromNameAndSourceFromExtension(string title)
    {
        AssertDetection(title, QualityDetectionSource.Extension, QualityDetectionSource.Name);
    }

    [Theory]
    [InlineData("Series Title S04E87 REPACK 720p HDTV x264 aAF", true, 2)]
    [InlineData("Series.Title.S04E87.REPACK.720p.HDTV.x264-aAF", true, 2)]
    [InlineData("Series.Title.S04E87.REPACK2.720p.HDTV.x264-aAF", true, 3)]
    [InlineData("Series.Title.S04E87.PROPER.720p.HDTV.x264-aAF", false, 2)]
    [InlineData("Series.Title.S01E07.RERIP.720p.BluRay.x264-DEMAND", true, 2)]
    [InlineData("Series.Title.S01E07.RERIP2.720p.BluRay.x264-DEMAND", true, 3)]
    [InlineData("[MTBB] Series Title - S02E02 - 027 - Episode Title [WEBDL-1080p v2][x264][AAC]", false, 2)]
    public void ParsesRevision(string title, bool isRepack, int version)
    {
        Revision revision = QualityParser.ParseQuality(title).Revision;
        Assert.Equal(version, revision.Version);
        Assert.Equal(isRepack, revision.IsRepack);
    }

    [Fact]
    public void TelesyncKeepsItsResolution()
    {
        Quality quality = QualityParser.ParseQuality("Movie.Title.3.2017.720p.TSRip.x264.AAC-Ozlem").Quality;
        Assert.Equal(Quality.Telesync, quality);
        Assert.Equal(720, quality.Resolution);
    }

    private static void AssertDetection(string title, QualityDetectionSource source, QualityDetectionSource resolution)
    {
        QualityModel result = QualityParser.ParseQuality(title);
        Assert.Equal(source, result.SourceDetectionSource);
        Assert.Equal(resolution, result.ResolutionDetectionSource);
    }
}
