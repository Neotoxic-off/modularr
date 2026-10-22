using Modularr.Core.Parsing.Episodes;

namespace Modularr.Core.Tests.Parsing.Episodes;

// Cases from the Sonarr IsPossibleSpecialEpisodeFixture (GPL-3.0).
public sealed class IsPossibleSpecialEpisodeTests
{
    [Theory]
    [InlineData("Title.the.Series.S02.Special-Inside.Chesters.Mill.HDTV.x264-BAJSKORV")]
    [InlineData("Title.the.Series.S02.Special-Inside.Chesters.Mill.720p.HDTV.x264-BAJSKORV")]
    [InlineData("Title.the.Series.S05.Special.HDTV.x264-2HD")]
    public void IsPossibleSpecialEpisodeShouldBeTrue(string title)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(title);
        Assert.NotNull(result);
        Assert.True(result.IsPossibleSpecialEpisode);
    }

    [Theory]
    [InlineData("Title.the.Series.S11E00.A.Christmas.Carol.Special.720p.HDTV-FieldOfView")]
    public void IsPossibleSpecialEpisodeShouldBeTrueIfE00Special(string title)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(title);
        Assert.NotNull(result);
        Assert.True(result.IsPossibleSpecialEpisode);
    }

    [Theory]
    [InlineData("Big.Special.Show.S05.HDTV.x264-2HD")]
    public void IsPossibleSpecialEpisodeShouldBeFalseForSpecialInTitle(string title)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(title);
        Assert.NotNull(result);
        Assert.False(result.IsPossibleSpecialEpisode);
    }
}
