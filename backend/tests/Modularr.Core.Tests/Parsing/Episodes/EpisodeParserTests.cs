using System.Security.Cryptography;
using System.Text;
using Modularr.Core.Parsing;
using Modularr.Core.Parsing.Episodes;

namespace Modularr.Core.Tests.Parsing.Episodes;

// The single [Test] cases of the Sonarr parser fixtures (GPL-3.0).
public sealed class EpisodeParserTests
{
    [Fact]
    public void ParsesDailyEpisodeIntoSeasonZero()
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle("A.Late.Talk.Show.2010.10.11.Johnny.Knoxville.iTouch-MW");
        Assert.NotNull(result);
        Assert.Equal([0], result.SeasonNumbers);
    }

    [Fact]
    public void DoesNotParseSeasonFolders() => Assert.Null(EpisodeParser.ParseTitle("Season 3"));

    [Fact]
    public void DoesNotParseMd5Hashes()
    {
        string hash = "CRAPPY TEST SEED";

        for (int i = 0; i < 100; i++)
        {
#pragma warning disable CA5351 // MD5 is the point: indexers post md5 names.
            hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(hash)));
#pragma warning restore CA5351
            Assert.Null(EpisodeParser.ParseTitle(hash));
        }
    }

    [Fact]
    public void RemovesAccentsFromTitle() => Assert.Equal("seriaes", ReleaseTitle.CleanTitle("Seri\u00E0es"));

    [Fact]
    public void RemovesAFromMiddleOfTitle()
    {
        foreach (string dirty in (string[])["word.a.word", "word a word", "word-a-word"])
        {
            Assert.Equal("wordword", ReleaseTitle.CleanTitle(dirty));
        }
    }

    [Fact]
    public void DoesNotCleanTrailingA() => Assert.Equal("seriestitlea", ReleaseTitle.CleanTitle("Series Title A"));

    [Fact]
    public void DoesNotIncludeExtensionInReleaseGroup()
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParsePath(@"C:\Test\Doctor.Series.2005.s01e01.internal.bdrip.x264-archivist.mkv".AsOsAgnostic());
        Assert.NotNull(result);
        Assert.Equal("archivist", result.ReleaseGroup);
    }

    [Fact]
    public void FilesWithoutSeriesTitleAreNotSpecials() =>
        Assert.False(new ParsedEpisodeInfo { EpisodeNumbers = [7], SeasonNumbers = [1], SeriesTitle = "" }.IsPossibleSpecialEpisode);

    [Fact]
    public void NoEpisodeNumbersIsPossibleSpecial() =>
        Assert.True(new ParsedEpisodeInfo { SeasonNumbers = [1], SeriesTitle = "" }.IsPossibleSpecialEpisode);

    [Fact]
    public void E00InRegularSeasonIsPossibleSceneSeasonSpecial() =>
        Assert.True(new ParsedEpisodeInfo { SeasonNumbers = [2], EpisodeNumbers = [0] }.IsPossibleSceneSeasonSpecial);

    [Fact]
    public void E00InSeasonZeroIsNotSceneSeasonSpecial() =>
        Assert.False(new ParsedEpisodeInfo { SeasonNumbers = [0], EpisodeNumbers = [0] }.IsPossibleSceneSeasonSpecial);

    [Fact]
    public void E00WithoutSeasonIsNotSceneSeasonSpecial() =>
        Assert.False(new ParsedEpisodeInfo { SeasonNumbers = [], EpisodeNumbers = [0] }.IsPossibleSceneSeasonSpecial);

    [Fact]
    public void EpisodeWithoutSeasonOrSeriesTitleIsNotSpecial() =>
        Assert.False(new ParsedEpisodeInfo { SeriesTitle = "", SeasonNumbers = [], EpisodeNumbers = [7] }.IsPossibleSpecialEpisode);

    [Fact]
    public void EpisodeInSeasonZeroWithoutSeriesTitleIsPossibleSpecial() =>
        Assert.True(new ParsedEpisodeInfo { SeriesTitle = "", SeasonNumbers = [0], EpisodeNumbers = [7] }.IsPossibleSpecialEpisode);

    [Fact]
    public void FormatsEpisodeWithFirstSeason() =>
        Assert.Contains("S02E05-06", new ParsedEpisodeInfo { SeriesTitle = "Series Title", SeasonNumbers = [2], EpisodeNumbers = [5, 6] }.ToString(), StringComparison.Ordinal);

    [Fact]
    public void TitleWithoutYearHasYearZero() => Assert.Equal(0, ParseTitleInfo("Series.Title.S01E01.pilot.720p.hdtv").Year);

    [Fact]
    public void TitleWithoutYearKeepsSameTitle()
    {
        SeriesTitleInfo info = ParseTitleInfo("Series.Title.S01E01.pilot.720p.hdtv");
        Assert.Equal(info.Title, info.TitleWithoutYear);
    }

    [Fact]
    public void TitleWithYearHasYear() => Assert.Equal(2004, ParseTitleInfo("Series.Title.2004.S01E01.pilot.720p.hdtv").Year);

    [Fact]
    public void TitleWithYearKeepsYearInTitle() => Assert.Equal("Series Title 2004", ParseTitleInfo("Series.Title.2004.S01E01.pilot.720p.hdtv").Title);

    [Fact]
    public void TitleWithoutYearDropsYear() => Assert.Equal("Series Title", ParseTitleInfo("Series.Title.2004.S01E01.pilot.720p.hdtv").TitleWithoutYear);

    private static SeriesTitleInfo ParseTitleInfo(string title)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(title);
        Assert.NotNull(result);
        return result.SeriesTitleInfo;
    }
}
