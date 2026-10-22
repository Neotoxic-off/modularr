// Cases from the Sonarr and Radarr AnimeVersionFixture (GPL-3.0).
using Modularr.Core.Parsing;

namespace Modularr.Core.Tests.Parsing;

public sealed class AnimeVersionTests
{
    [Theory]
    [InlineData("Anime Title - S01E01 - (BD 1080p HEVC FLAC) [Dual Audio] [Group]", 1)]
    [InlineData("Anime Title - S01E01v2 - (BD 1080p HEVC FLAC) [Dual Audio] [Group]", 2)]
    [InlineData("Anime Title - S01E01 v2 - (BD 1080p HEVC FLAC) [Dual Audio] [Group]", 2)]
    [InlineData("[SubsPlease] Anime Title - 01 (1080p) [B1F227CF]", 1)]
    [InlineData("[SubsPlease] Anime Title - 01v2 (1080p) [B1F227CF]", 2)]
    [InlineData("[SubsPlease] Anime Title - 01 v2 (1080p) [B1F227CF]", 2)]
    [InlineData("Anime Title - 2018 - (BD 1080p HEVC FLAC) [Dual Audio] [Group]", 1)]
    [InlineData("Anime Title - 2018v2 - (BD 1080p HEVC FLAC) [Dual Audio] [Group]", 2)]
    [InlineData("Anime Title - 2018 v2 - (BD 1080p HEVC FLAC) [Dual Audio] [Group]", 2)]
    public void ParsesAnimeVersion(string title, int version)
    {
        Assert.Equal(version, QualityParser.ParseQuality(title).Revision.Version);
    }
}
