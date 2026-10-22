using Modularr.Core.Languages;
using Modularr.Core.Parsing.Episodes;

namespace Modularr.Core.Tests.Parsing.Episodes;

// Cases from the Sonarr SeasonTitleParserFixture (GPL-3.0).
public sealed class SeasonTitleParserTests
{
    [Theory]
    [InlineData("[Group] Series Title Curse (BS11 1280x720 x264 AAC)", "Series Title Curse")]
    [InlineData("[Group] Series Title Curse (BDRip 1920x1080 x264 FLAC) [ABCD1234]", "Series Title Curse")]
    [InlineData("[Group] Series.Title - Curse (BD, 720p)", "Series.Title - Curse")]
    [InlineData("Series Title Curse [Bluray.1080p.HEVC.AAC.ITA.FLAC.JAP.Sub.Ita]", "Series Title Curse")]
    [InlineData("[Group] Series Title: Curse [Dual Audio 10bit 1080p][HEVC-x265]", "Series Title: Curse")]
    public void ShouldReturnTheTitleWhenThereIsNoSeasonOrEpisode(string releaseTitle, string expectedTitle)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseSeasonTitle(releaseTitle);
        Assert.NotNull(result);
        Assert.Equal(expectedTitle, result.SeriesTitle);
        Assert.True(result.FullSeason);
        Assert.True(result.IsSeasonTitle);
        Assert.Empty(result.SeasonNumbers);
        Assert.Empty(result.EpisodeNumbers);
        Assert.Empty(result.AbsoluteEpisodeNumbers);
    }

    [Theory]
    [InlineData("[Group] Series Title Curse (BD 1280x720 x264 AAC)")]
    [InlineData("[Group] Series Title: Curse [Dual Audio 10bit 1080p][HEVC-x265]")]
    public void ShouldPopulateTheFieldsTheDecisionEngineReads(string releaseTitle)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseSeasonTitle(releaseTitle);
        Assert.NotNull(result);
        Assert.NotNull(result.Quality);
        Assert.NotEmpty(result.Languages);
    }

    [Theory]
    [InlineData("[Group][Series Title: Curse][BDRip][1080P_x265(10bit)-FLAC][ALL]")]
    [InlineData("[Group][Series Title Curse][1080p][x264]")]
    [InlineData("[Group] Series Title Curse () (1080p)")]
    public void ShouldNotReturnATitleWhenStrippingLeavesMetadata(string releaseTitle)
    {
        Assert.Null(EpisodeParser.ParseSeasonTitle(releaseTitle));
    }

    [Theory]
    [InlineData("[Group] Series Title Curse (OVA)")]
    [InlineData("[Group] Series Title Curse [OVA]")]
    [InlineData("[Group] Series Title Curse (Special)")]
    [InlineData("[Group] Series Title Curse (NCOP)")]
    [InlineData("[Group] Series Title Curse (NCED)")]
    public void ShouldNotReturnATitleForAnExtra(string releaseTitle)
    {
        Assert.Null(EpisodeParser.ParseSeasonTitle(releaseTitle));
    }

    [Theory]
    [InlineData("Series Title S02E05 1080p WEB-DL")]
    [InlineData("[Group] Series Title - 05 (1080p)")]
    [InlineData("Series Title S02 1080p WEB-DL")]
    public void ShouldNotFlagAReleaseTheRegexesCanParse(string releaseTitle)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(releaseTitle);
        Assert.NotNull(result);
        Assert.False(result.IsSeasonTitle);
    }

    [Theory]
    [InlineData("THIS SHOULD NEVER PARSE")]
    [InlineData("Vrq6e1Aba3U amCjuEgV5R2QvdsLEGYF3YQAQkw8")]
    [InlineData("[Group] Series Title Curse (BS11 1280x720 x264 AAC)")]
    public void ShouldLeaveParseTitleRejectingWhatItRejectsToday(string releaseTitle)
    {
        Assert.Null(EpisodeParser.ParseTitle(releaseTitle));
    }
}
