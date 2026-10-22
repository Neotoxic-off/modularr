using Modularr.Core.Parsing.Episodes;

namespace Modularr.Core.Tests.Parsing.Episodes;

// Cases from the Sonarr ParsedEpisodeInfoFixture (GPL-3.0).
public sealed class ParsedEpisodeInfoTests
{
    [Theory]
    [InlineData(new[] { 1, 2, 3 }, "Season 01-03")]
    [InlineData(new[] { 2 }, "Season 02")]
    [InlineData(new int[0], "[Unknown Season]")]
    public void ShouldFormatFullSeason(int[] seasonNumbers, string expected)
    {
        ParsedEpisodeInfo info = new() { SeriesTitle = "Series Title", SeasonNumbers = seasonNumbers, FullSeason = true };
        Assert.Contains(expected, info.ToString(), StringComparison.Ordinal);
    }
}
