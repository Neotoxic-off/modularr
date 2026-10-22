using Modularr.Core.Parsing.Episodes;

namespace Modularr.Core.Tests.Parsing.Episodes;

// Cases from the Sonarr SubGroupParserFixture (GPL-3.0).
public sealed class SubGroupParserTests
{
    [Theory]
    [InlineData("[GHOST][1080p] Series - 25 [BD HEVC 10bit Dual Audio AC3][AE0ADDBA]", "GHOST")]
    public void ShouldParseSubGroupFromTitleAsReleaseGroup(string title, string expected)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(title);
        Assert.NotNull(result);
        Assert.Equal(expected, result.ReleaseGroup);
    }
}
