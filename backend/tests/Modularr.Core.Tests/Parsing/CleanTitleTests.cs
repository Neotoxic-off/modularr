using Modularr.Core.Parsing;

namespace Modularr.Core.Tests.Parsing;

// Cases from the Sonarr NormalizeSeriesTitleFixture (GPL-3.0).
public sealed class CleanTitleTests
{
    [Theory]
    [InlineData("Series", "series")]
    [InlineData("Series (2009)", "series2009")]
    [InlineData("Series.2010", "series2010")]
    [InlineData("Series_and_Title_Sonarr", "seriestitlesonarr")]
    public void ShouldNormalizeSeriesTitle(string parsedSeriesName, string seriesName)
    {
        Assert.Equal(seriesName, ReleaseTitle.CleanTitle(parsedSeriesName));
    }

    [Theory]
    [InlineData("CaPitAl", "capital")]
    [InlineData("peri.od", "period")]
    [InlineData("this.^&%^**$%@#$!That", "thisthat")]
    [InlineData("test/test", "testtest")]
    [InlineData("90210", "90210")]
    [InlineData("24", "24")]
    [InlineData("Test: Something à Deux", "testsomethingdeux")]
    [InlineData("Parler à", "parlera")]
    [InlineData("Ríkið", "rikid")]
    public void ShouldRemoveSpecialCharactersAndCasing(string dirty, string clean)
    {
        Assert.Equal(clean, ReleaseTitle.CleanTitle(dirty));
    }

    [Theory]
    [InlineData("the")]
    [InlineData("and")]
    [InlineData("or")]
    [InlineData("an")]
    [InlineData("of")]
    public void ShouldRemoveCommonWordsFromMiddleOfTitle(string word)
    {
        string[] formats = ["word.{0}.word", "word {0} word", "word-{0}-word"];

        foreach (string format in formats)
        {
            Assert.Equal("wordword", ReleaseTitle.CleanTitle(format.Replace("{0}", word, StringComparison.Ordinal)));
        }
    }

    [Theory]
    [InlineData("the")]
    [InlineData("and")]
    [InlineData("or")]
    [InlineData("an")]
    [InlineData("of")]
    public void ShouldNotRemoveCommonWordsFromEndOfTitle(string word)
    {
        string[] formats = ["word.word.{0}", "word-word-{0}", "word-word {0}"];

        foreach (string format in formats)
        {
            Assert.Equal("wordword" + word, ReleaseTitle.CleanTitle(format.Replace("{0}", word, StringComparison.Ordinal)));
        }
    }

    [Theory]
    [InlineData("the")]
    [InlineData("and")]
    [InlineData("or")]
    [InlineData("a")]
    [InlineData("an")]
    [InlineData("of")]
    public void ShouldNotRemoveCommonWordsInTheMiddleOfWord(string word)
    {
        string[] formats = ["word.{0}word", "word {0}word", "word-{0}word", "word{0}.word", "word{0}-word"];

        foreach (string format in formats)
        {
            Assert.Equal("word" + word + "word", ReleaseTitle.CleanTitle(format.Replace("{0}", word, StringComparison.Ordinal)));
        }
    }

    [Theory]
    [InlineData("The Series", "theseries")]
    [InlineData("The Series Show With Sonarr Dev", "theseriesshowwithsonarrdev")]
    [InlineData("The.Series.Show", "theseriesshow")]
    public void ShouldNotRemoveFromTheBeginningOfTheTitle(string parsedSeriesName, string seriesName)
    {
        Assert.Equal(seriesName, ReleaseTitle.CleanTitle(parsedSeriesName));
    }

    [Theory]
    [InlineData("the")]
    [InlineData("and")]
    [InlineData("or")]
    [InlineData("a")]
    [InlineData("an")]
    [InlineData("of")]
    public void ShouldNotCleanWordFromBeginningOfString(string word)
    {
        string[] formats = ["{0}.word.word", "{0}-word-word", "{0} word word"];

        foreach (string format in formats)
        {
            Assert.Equal(word + "wordword", ReleaseTitle.CleanTitle(format.Replace("{0}", word, StringComparison.Ordinal)));
        }
    }

    [Theory]
    [InlineData("3%", "3percent")]
    [InlineData("Series Top & 100% Coding Developers", "seriestop100percentcodingdevelopers")]
    [InlineData("Series Title What's Your F@%king Deal?!", "seriestitlewhatsyourfkingdeal")]
    public void ShouldReplacePercentSignWithPercentFollowingNumbers(string input, string expected)
    {
        Assert.Equal(expected, ReleaseTitle.CleanTitle(input));
    }
}
