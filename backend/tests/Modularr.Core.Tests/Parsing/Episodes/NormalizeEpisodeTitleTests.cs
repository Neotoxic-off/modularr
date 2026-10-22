using Modularr.Core.Parsing.Episodes;

namespace Modularr.Core.Tests.Parsing.Episodes;

// Cases from the Sonarr NormalizeEpisodeTitleFixture (GPL-3.0).
public sealed class NormalizeEpisodeTitleTests
{
    [Theory]
    [InlineData("Episode Title", "episode title")]
    [InlineData("A.B,C;", "a b c")]
    [InlineData("Episode  Title", "episode title")]
    [InlineData("French Title (1)", "french title")]
    [InlineData("Series.Title.S01.Special.Episode.Title.720p.HDTV.x264-Sonarr", "episode title")]
    [InlineData("Series.Title.S01E00.Episode.Title.720p.HDTV.x264-Sonarr", "episode title")]
    public void ShouldNormalizeEpisodeTitle(string input, string expected)
    {
        Assert.Equal(expected, EpisodeParser.NormalizeEpisodeTitle(input));
    }
}
