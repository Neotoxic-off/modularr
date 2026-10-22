using Modularr.Core.Parsing.Episodes;

namespace Modularr.Core.Tests.Parsing.Episodes;

// Cases from the Sonarr MiniSeriesEpisodeParserFixture (GPL-3.0).
public sealed class MiniSeriesEpisodeParserTests
{
    [Theory]
    [InlineData("The.Big.Series.Leader.Part.2.DSR.XviD-SYS", "The Big Series Leader", 2)]
    [InlineData("kill-roy-was-here-e07-720p", "kill-roy-was-here", 7)]
    [InlineData("Series and Show 2012 Part 1 REPACK 720p HDTV x264 2HD", "Series and Show 2012", 1)]
    [InlineData("Series Show.2016.E04.Power.720p.WEB-DL.DD5.1.H.264-MARS", "Series Show 2016", 4)]
    public void ShouldParseMiniSeriesEpisode(string postTitle, string title, int episodeNumber)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(postTitle);
        Assert.NotNull(result);
        Assert.Equal(episodeNumber, Assert.Single(result.EpisodeNumbers));
        Assert.Equal([1], result.SeasonNumbers);
        Assert.Equal(title, result.SeriesTitle);
        Assert.Empty(result.AbsoluteEpisodeNumbers);
        Assert.False(result.FullSeason);
    }

    [Theory]
    [InlineData("It's a Series Title.E56.190121.720p-NEXT.mp4", "It's a Series Title", 56, "2019-01-21")]
    [InlineData("My Only Series Title.E37.190120.1080p-NEXT.mp4", "My Only Series Title", 37, "2019-01-20")]
    [InlineData("Series.E191.190121.720p-NEXT.mp4", "Series", 191, "2019-01-21")]
    [InlineData("The Series Title Challenge.E932.190120.720p-NEXT.mp4", "The Series Title Challenge", 932, "2019-01-20")]
    [InlineData("백번의 추억.E12.251019.1080p-F1RST", "백번의 추억", 12, "2025-01-19")]
    [InlineData("나는 SOLO <나는 솔로>.E223.251015.1080p-F1RST", "나는 SOLO 나는 솔로", 223, "2025-10-15")]
    [InlineData("나는 SOLO 그 후, 사랑은 계속된다(나솔사계).E135.251016.1080p-F1RST", "나는 SOLO 그 후, 사랑은 계속된다(나솔사계)", 135, "2025-10-16")]
    public void ShouldParseKoreanSeriesEpisode(string postTitle, string title, int episodeNumber, string _)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(postTitle);
        Assert.NotNull(result);
        Assert.Equal(episodeNumber, Assert.Single(result.EpisodeNumbers));
        Assert.Equal([1], result.SeasonNumbers);
        Assert.Equal(title, result.SeriesTitle);
        Assert.Empty(result.AbsoluteEpisodeNumbers);
        Assert.False(result.FullSeason);
    }
}
