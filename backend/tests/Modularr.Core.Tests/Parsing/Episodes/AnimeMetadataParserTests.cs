using Modularr.Core.Parsing.Episodes;

namespace Modularr.Core.Tests.Parsing.Episodes;

// Cases from the Sonarr AnimeMetadataParserFixture (GPL-3.0).
public sealed class AnimeMetadataParserTests
{
    [Theory]
    [InlineData("[SubDESU]_Show_Title_DxD_07_(1280x720_x264-AAC)_[6B7FD717]", "SubDESU", "6B7FD717")]
    [InlineData("[Chihiro]_Show_Title!!_-_06_[848x480_H.264_AAC][859EEAFA]", "Chihiro", "859EEAFA")]
    [InlineData("[Underwater]_Show_Title_-_12_(720p)_[5C7BC4F9]", "Underwater", "5C7BC4F9")]
    [InlineData("[HorribleSubs]_Show_Title_-_33_[720p]", "HorribleSubs", null)]
    [InlineData("[HorribleSubs] Show-Title - 13 [1080p].mkv", "HorribleSubs", null)]
    [InlineData("[Doremi].Show.Title.5.Go.Go!.31.[1280x720].[C65D4B1F].mkv", "Doremi", "C65D4B1F")]
    [InlineData("[Doremi].Show.Title.5.Go.Go!.31[1280x720].[C65D4B1F]", "Doremi", "C65D4B1F")]
    [InlineData("[Doremi].Show.Title.5.Go.Go!.31.[1280x720].mkv", "Doremi", null)]
    [InlineData("[K-F] Series Title 214", "K-F", null)]
    [InlineData("[K-F] Series Title S10E14 214", "K-F", null)]
    [InlineData("[K-F] Series Title 10x14 214", "K-F", null)]
    [InlineData("[K-F] Series Title 214 10x14", "K-F", null)]
    [InlineData("Series Title - 031 - The Resolution to Kill [Lunar].avi", "Lunar", null)]
    [InlineData("[ACX]Series Title 01 Episode Name [Kosaka] [9C57891E].mkv", "ACX", "9C57891E")]
    [InlineData("[S-T-D] Series Title! - 06 (1280x720 10bit AAC) [59B3F2EA].mkv", "S-T-D", "59B3F2EA")]
    [InlineData("[SubDESU]_Show_Title_DxD_07_(1280x720_x264-AAC)_(6B7FD717)", "SubDESU", "6B7FD717")]
    [InlineData("[Chihiro]_Show_Title!!_-_06_[848x480_H.264_AAC](859EEAFA)", "Chihiro", "859EEAFA")]
    [InlineData("[Underwater]_Show_Title_-_12_(720p)_(5C7BC4F9)", "Underwater", "5C7BC4F9")]
    [InlineData("[Doremi].Show.Title.5.Go.Go!.31.[1280x720].(C65D4B1F).mkv", "Doremi", "C65D4B1F")]
    [InlineData("[Doremi].Show.Title.5.Go.Go!.31[1280x720].(C65D4B1F)", "Doremi", "C65D4B1F")]
    [InlineData("[ACX]Series Title 01 Episode Name [Kosaka] (9C57891E).mkv", "ACX", "9C57891E")]
    [InlineData("[S-T-D] Series Title! - 06 (1280x720 10bit AAC) (59B3F2EA).mkv", "S-T-D", "59B3F2EA")]
    public void ShouldParseReleasegroupAndHash(string postTitle, string subGroup, string? hash)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(postTitle);
        Assert.NotNull(result);
        Assert.Equal(subGroup, result.ReleaseGroup);
        Assert.Equal(hash, result.ReleaseHash);
    }

    [Theory]
    [InlineData("[DHD] Series Title! - 08 (1280x720 10bit AAC) [8B00F2EA].mkv", "8B00F2EA")]
    [InlineData("[DHD] Series Title! - 10 (1280x720 10bit AAC) [10BBF2EA].mkv", "10BBF2EA")]
    [InlineData("[DHD] Series Title! - 08 (1280x720 10bit AAC) [008BF28B].mkv", "008BF28B")]
    [InlineData("[DHD] Series Title! - 10 (1280x720 10bit AAC) [000BF10B].mkv", "000BF10B")]
    [InlineData("[DHD] Series Title! - 08 (1280x720 8bit AAC) [8B8BF2EA].mkv", "8B8BF2EA")]
    [InlineData("[DHD] Series Title! - 10 (1280x720 8bit AAC) [10B10BEA].mkv", "10B10BEA")]
    public void ShouldParseReleaseHashesWith10bOr8b(string postTitle, string hash)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(postTitle);
        Assert.NotNull(result);
        Assert.Equal(hash, result.ReleaseHash);
    }
}
