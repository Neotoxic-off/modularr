using Modularr.Core.Parsing.Episodes;

namespace Modularr.Core.Tests.Parsing.Episodes;

// Cases from the Sonarr PathParserFixture (GPL-3.0).
public sealed class PathParserTests
{
    [Theory]
    [InlineData(@"z:\tv shows\series title (2003)\Season 3\S03E05 - Title.mkv", 3, 5)]
    [InlineData(@"z:\tv showsseries title\Season 16\S16E03 - The Title.mkv", 16, 3)]
    [InlineData(@"z:\tv shows\series title\Specials\S00E16 - Dear Title - SD TV.avi", 0, 16)]
    [InlineData(@"D:\shares\TV Shows\series title\Season 2\S02E21 - 94 Title - 720p TV.mkv", 2, 21)]
    [InlineData(@"D:\shares\TV Shows\Series (2003)\Season 2\S02E21.avi", 2, 21)]
    [InlineData("C:/Test/TV/Series.4x05.HDTV.XviD-LOL", 4, 5)]
    [InlineData(@"P:\TV Shows\Series\Season 6\S06E13 - 5 to 9 - 720p BluRay.mkv", 6, 13)]
    [InlineData(@"S:\TV Drop\Series - 10x11 - Title [SDTV]\1011 - Title.avi", 10, 11)]
    [InlineData(@"/TV Drop/Series - 10x11 - Title [SDTV]/1011 - Title.avi", 10, 11)]
    [InlineData(@"S:\TV Drop\Series Title - 10x12 - 24 Hours of Development [SDTV]\1012 - Hours of Development.avi", 10, 12)]
    [InlineData(@"/TV Drop/Series Title - 10x12 - 24 Hours of Development [SDTV]/1012 - Hours of Development.avi", 10, 12)]
    [InlineData(@"S:\TV Drop\Series Title - 10x12 - 24 Hours of Development [SDTV]\Hours of Development.avi", 10, 12)]
    [InlineData(@"/TV Drop/Series Title - 10x12 - 24 Hours of Development [SDTV]/Hours of Development.avi", 10, 12)]
    [InlineData(@"E:\Downloads\tv\Series.Title.S01E01.720p.HDTV\ajifajjjeaeaeqwer_eppj.avi", 1, 1)]
    [InlineData(@"C:\Test\Unsorted\Series.Title.S01E01.720p.HDTV\tbbt101.avi", 1, 1)]
    [InlineData(@"C:\Test\Unsorted\Series.Title.S02E19.720p.BluRay.x264-SiNNERS-RP\ba27283b17c00d01193eacc02a8ba98eeb523a76.mkv", 2, 19)]
    [InlineData(@"C:\Test\Unsorted\Series.Title.S02E18.720p.BluRay.x264-SiNNERS-RP\45a55debe3856da318cc35882ad07e43cd32fd15.mkv", 2, 18)]
    [InlineData(@"C:\Test\Series\Season 01\01 Pilot (1080p HD).mkv", 1, 1)]
    [InlineData(@"C:\Test\Series\Season 01\1 Pilot (1080p HD).mkv", 1, 1)]
    [InlineData(@"C:\Test\Series\Season 1\02 Honor Thy Father (1080p HD).m4v", 1, 2)]
    [InlineData(@"C:\Test\Series\Season 1\2 Honor Thy Developer (1080p HD).m4v", 1, 2)]
    [InlineData(@"C:\Test\Series\Season 2 - Total Series Action\01. Total Series Action - Episode 1 - Monster Cash.mkv", 2, 1)]
    [InlineData(@"C:\Test\Series\Season 2\01. Total Series Action - Episode 1 - Monster Cash.mkv", 2, 1)]
    [InlineData(@"C:\Test\Series\Season 1\02.04.24 - S01E01 - The Rabbit Hole", 1, 1)]
    [InlineData(@"C:\Test\Series\Season 1\8 Series Rules - S01E01 - Pilot", 1, 1)]
    [InlineData(@"C:\Test\Series Title\Series Title S02 01.mkv", 2, 1)]
    [InlineData(@"C:\Test\Series Title\Series Title S2 1.mkv", 2, 1)]
    [InlineData(@"C:\Test\Series Title\Series Title S2 10.mkv", 2, 10)]
    [InlineData(@"C:\Test\Series Title\S02 01.mkv", 2, 1)]
    [InlineData(@"C:\Test\Series Title\Series Title S02 10.mkv", 2, 10)]
    [InlineData(@"C:\Test\Series Title S01 02\Series Title S02 01 1080p WEB-DL.mkv", 2, 1)]
    public void ShouldParseFromPath(string path, int season, int episode)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParsePath(path.AsOsAgnostic());
        Assert.NotNull(result);
        Assert.Equal(episode, Assert.Single(result.EpisodeNumbers));
        Assert.Equal([season], result.SeasonNumbers);
        Assert.Empty(result.AbsoluteEpisodeNumbers);
        Assert.False(result.FullSeason);
    }

    [Theory]
    [InlineData("01-03\\The Series Title (2010) - 1x01-02-03 - Episode Title HDTV-720p Proper", "The Series Title (2010)", 1, new[] { 1, 2, 3 })]
    [InlineData("Season 2\\E05-06 - Episode Title HDTV-720p Proper", "", 2, new[] { 5, 6 })]
    public void ShouldParseMultiEpisodeFromPath(string path, string title, int season, int[] episodes)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParsePath(path.AsOsAgnostic());
        Assert.NotNull(result);
        Assert.Equal(title, result.SeriesTitle);
        Assert.Equal([season], result.SeasonNumbers);
        Assert.Equal(episodes, result.EpisodeNumbers);
        Assert.Empty(result.AbsoluteEpisodeNumbers);
        Assert.False(result.FullSeason);
    }
}
