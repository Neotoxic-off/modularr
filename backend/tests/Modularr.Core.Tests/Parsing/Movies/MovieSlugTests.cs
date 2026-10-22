// Cases from the Radarr SlugParserFixture (GPL-3.0).
using Modularr.Core.Parsing.Movies;

namespace Modularr.Core.Tests.Parsing.Movies;

public sealed class MovieSlugTests
{
    [Theory]
    [InlineData("tèst", "test")]
    [InlineData("têst", "test")]
    [InlineData("tëst", "test")]
    [InlineData("tËst", "test")]
    [InlineData("áccent", "accent")]
    [InlineData("àccent", "accent")]
    [InlineData("âccent", "accent")]
    [InlineData("Äccent", "accent")]
    [InlineData("åccent", "accent")]
    [InlineData("acceñt", "accent")]
    // Radarr expected "test": its RemoveAccent drops letters FormD cannot split; the shared RemoveDiacritics (Sonarr) transliterates them.
    [InlineData("ßtest", "sstest")]
    [InlineData("œtest", "oetest")]
    [InlineData("Œtest", "oetest")]
    [InlineData("Øtest", "otest")]
    public void ReplacesAccents(string input, string result)
    {
        Assert.Equal(result, MovieTitleParser.ToUrlSlug(input));
    }

    [Theory]
    [InlineData("Test'Result")]
    [InlineData("Test$Result")]
    [InlineData("Test(Result")]
    [InlineData("Test)Result")]
    [InlineData("Test*Result")]
    [InlineData("Test?Result")]
    [InlineData("Test/Result")]
    [InlineData("Test=Result")]
    [InlineData("Test\\Result")]
    public void ReplacesSpecialCharacters(string input)
    {
        Assert.Equal("testresult", MovieTitleParser.ToUrlSlug(input));
    }

    [Theory]
    [InlineData("ThIS IS A MiXeD CaSe SensItIvE ValUe")]
    public void LowercasesCapitals(string input)
    {
        Assert.Equal("this-is-a-mixed-case-sensitive-value", MovieTitleParser.ToUrlSlug(input));
    }

    [Theory]
    [InlineData("test----")]
    [InlineData("test____")]
    [InlineData("test-_--_")]
    public void TrimsTrailingDashesAndUnderscores(string input)
    {
        Assert.Equal("test", MovieTitleParser.ToUrlSlug(input));
    }

    [Theory]
    [InlineData("test result")]
    [InlineData("test     result")]
    public void ReplacesSpacesWithDash(string input)
    {
        Assert.Equal("test-result", MovieTitleParser.ToUrlSlug(input));
    }

    [Theory]
    [InlineData("test     result", "test-result")]
    [InlineData("test-----result", "test-result")]
    [InlineData("test_____result", "test_result")]
    public void ReplacesDoubleOccurrence(string input, string result)
    {
        Assert.Equal(result, MovieTitleParser.ToUrlSlug(input));
    }

    [Theory]
    [InlineData("Test'Result")]
    [InlineData("Test$Result")]
    [InlineData("Test(Result")]
    [InlineData("Test)Result")]
    [InlineData("Test*Result")]
    [InlineData("Test?Result")]
    [InlineData("Test/Result")]
    [InlineData("Test=Result")]
    [InlineData("Test\\Result")]
    public void ReplacesSpecialCharactersWithDashWhenEnabled(string input)
    {
        Assert.Equal("test-result", MovieTitleParser.ToUrlSlug(input, true));
    }

    [Theory]
    [InlineData("Test'Result")]
    [InlineData("Test$Result")]
    [InlineData("Test(Result")]
    [InlineData("Test)Result")]
    [InlineData("Test*Result")]
    [InlineData("Test?Result")]
    [InlineData("Test/Result")]
    [InlineData("Test=Result")]
    [InlineData("Test\\Result")]
    public void DoesNotReplaceSpecialCharactersWithDashWhenDisabled(string input)
    {
        Assert.Equal("testresult", MovieTitleParser.ToUrlSlug(input, false));
    }

    [Theory]
    [InlineData("test----", "-_", "test")]
    [InlineData("test____", "-_", "test")]
    [InlineData("test-_-_", "-_", "test")]
    [InlineData("test----", "-", "test")]
    [InlineData("test____", "-", "test____")]
    [InlineData("test-_-_", "-", "test-_-_")]
    [InlineData("test----", "_", "test----")]
    [InlineData("test____", "_", "test")]
    [InlineData("test-_-_", "_", "test-_-")]
    [InlineData("test----", "", "test----")]
    [InlineData("test____", "", "test____")]
    [InlineData("test-_-_", "", "test-_-_")]
    public void TrimsTrailingDashesAndUnderscoresBasedOnList(string input, string trimList, string result)
    {
        Assert.Equal(result, MovieTitleParser.ToUrlSlug(input, false, trimList, string.Empty));
    }

    [Theory]
    [InlineData("test----result", "-_", "test-result")]
    [InlineData("test____result", "-_", "test_result")]
    [InlineData("test_-_-result", "-_", "test-result")]
    [InlineData("test-_-_result", "-_", "test_result")]
    [InlineData("test----result", "-", "test-result")]
    [InlineData("test____result", "-", "test____result")]
    [InlineData("test-_-_result", "-", "test-_-_result")]
    [InlineData("test----result", "_", "test----result")]
    [InlineData("test____result", "_", "test_result")]
    [InlineData("test-_-_result", "_", "test-_-_result")]
    [InlineData("test----result", "", "test----result")]
    [InlineData("test____result", "", "test____result")]
    [InlineData("test-_-_result", "", "test-_-_result")]
    public void ReplacesDuplicateCharactersBasedOnList(string input, string deduplicateChars, string result)
    {
        Assert.Equal(result, MovieTitleParser.ToUrlSlug(input, false, string.Empty, deduplicateChars));
    }

    [Theory]
    [InlineData(null, "-_")]
    [InlineData("-_", null)]
    [InlineData("", "-_")]
    [InlineData("-_", "")]
    public void HandlesNullOrEmptyTrimAndDedupeParameters(string? trimEndChars, string? deduplicateChars)
    {
        Assert.Equal("test", MovieTitleParser.ToUrlSlug("test", false, trimEndChars, deduplicateChars));
    }
}
