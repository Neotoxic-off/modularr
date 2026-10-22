// Cases from the Radarr NormalizeTitleFixture (GPL-3.0).
using Modularr.Core.Parsing.Movies;

namespace Modularr.Core.Tests.Parsing.Movies;

public sealed class MovieTitleNormalizationTests
{
    [Theory]
    [InlineData("Conan", "conan")]
    [InlineData("Castle (2009)", "castle2009")]
    [InlineData("Parenthood.2010", "parenthood2010")]
    [InlineData("Law_and_Order_SVU", "lawordersvu")]
    public void NormalizesTitle(string parsedTitle, string title)
    {
        Assert.Equal(title, MovieTitleParser.CleanMovieTitle(parsedTitle));
    }

    [Theory]
    [InlineData("CaPitAl", "capital")]
    [InlineData("peri.od", "period")]
    [InlineData("this.^&%^**$%@#$!That", "thisthat")]
    [InlineData("test/test", "testtest")]
    [InlineData("90210", "90210")]
    [InlineData("24", "24")]
    [InlineData("I'm a cyborg, but that's OK", "imcyborgbutthatsok")]
    [InlineData("Im a cyborg, but thats ok", "imcyborgbutthatsok")]
    [InlineData("Test: Something à Deux", "testsomethingdeux")]
    [InlineData("Parler à", "parlera")]
    public void RemovesSpecialCharactersAndCasing(string dirty, string clean)
    {
        Assert.Equal(clean, MovieTitleParser.CleanMovieTitle(dirty));
    }

    [Theory]
    [InlineData("the")]
    [InlineData("and")]
    [InlineData("or")]
    [InlineData("an")]
    [InlineData("of")]
    public void RemovesCommonWordsFromMiddleOfTitle(string word)
    {
        AssertCleaned(["word.{0}.word", "word {0} word", "word-{0}-word"], word, "wordword");
    }

    [Theory]
    [InlineData("the")]
    [InlineData("and")]
    [InlineData("or")]
    [InlineData("an")]
    [InlineData("of")]
    public void DoesNotRemoveCommonWordsFromEndOfTitle(string word)
    {
        AssertCleaned(["word.word.{0}", "word-word-{0}", "word-word {0}"], word, "wordword" + word);
    }

    [Fact]
    public void RemovesAFromMiddleOfTitle()
    {
        AssertCleaned(["word.{0}.word", "word {0} word", "word-{0}-word"], "a", "wordword");
    }

    [Fact]
    public void DoesNotRemoveAWhenAtStartOfAcronym()
    {
        AssertCleaned(["word.{0}.N.K.L.E.word", "word {0} N K L E word", "word-{0}-N-K-L-E-word"], "a", "wordankleword");
    }

    [Fact]
    public void DoesNotRemoveAWhenAtEndOfAcronym()
    {
        AssertCleaned(["word.N.K.L.E.{0}.word", "word N K L E {0} word", "word-N-K-L-E-{0}-word"], "a", "wordnkleaword");
    }

    [Theory]
    [InlineData("the")]
    [InlineData("and")]
    [InlineData("or")]
    [InlineData("a")]
    [InlineData("an")]
    [InlineData("of")]
    public void DoesNotRemoveCommonWordsInTheMiddleOfWord(string word)
    {
        AssertCleaned(["word.{0}word", "word {0}word", "word-{0}word", "word{0}.word", "word{0}-word"], word, "word" + word + "word");
    }

    [Theory]
    [InlineData("The Office", "theoffice")]
    [InlineData("The Tonight Show With Jay Leno", "thetonightshowwithjayleno")]
    [InlineData("The.Daily.Show", "thedailyshow")]
    public void DoesNotRemoveFromTheBeginningOfTheTitle(string parsedTitle, string title)
    {
        Assert.Equal(title, MovieTitleParser.CleanMovieTitle(parsedTitle));
    }

    [Theory]
    [InlineData("the")]
    [InlineData("and")]
    [InlineData("or")]
    [InlineData("a")]
    [InlineData("an")]
    [InlineData("of")]
    public void DoesNotCleanWordFromBeginningOfString(string word)
    {
        AssertCleaned(["{0}.word.word", "{0}-word-word", "{0} word word"], word, word + "wordword");
    }

    [Fact]
    public void DoesNotCleanTrailingA()
    {
        Assert.Equal("tokyoghoula", MovieTitleParser.CleanMovieTitle("Tokyo Ghoul A"));
    }

    // Not upstream (Radarr has no fixture for these): sort title and language-token normalisation.
    [Theory]
    [InlineData("The Movie: Part 2!", "movie part 2")]
    [InlineData("A Bug's Life", "bugs life")]
    public void NormalizesSortTitle(string title, string expected)
    {
        Assert.Equal(expected, MovieTitleParser.NormalizeTitle(title));
    }

    [Theory]
    [InlineData("Movie.Title.Special.Edition.German", "movie title german")]
    [InlineData("Movie Title - Part 2 (French)", "movie title 2 french")]
    public void NormalizesMovieTitleTokens(string title, string expected)
    {
        Assert.Equal(expected, MovieTitleParser.NormalizeMovieTitle(title));
    }

    [Fact]
    public void ReplacesGermanUmlauts()
    {
        Assert.Equal("Aerger Uebel Oel Strasse", MovieTitleParser.ReplaceGermanUmlauts("Ärger Übel Öl Straße"));
    }

    private static void AssertCleaned(string[] formats, string word, string expected)
    {
        foreach (string format in formats)
        {
            Assert.Equal(expected, MovieTitleParser.CleanMovieTitle(format.Replace("{0}", word, StringComparison.Ordinal)));
        }
    }
}
