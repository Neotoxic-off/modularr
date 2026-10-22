// Cases merged from the Sonarr and Radarr IsoLanguagesFixture (GPL-3.0).
using Modularr.Core.Languages;

namespace Modularr.Core.Tests.Languages;

public sealed class IsoLanguagesCaseTests
{
    [Theory]
    [InlineData("en", "English")]
    [InlineData("eng", "English")]
    [InlineData("en-US", "English")]
    [InlineData("en-GB", "English")]
    [InlineData("pt", "Portuguese")]
    [InlineData("por", "Portuguese")]
    [InlineData("pt-PT", "Portuguese")]
    [InlineData("cze", "Czech")]
    [InlineData("ur", "Urdu")]
    [InlineData("urd", "Urdu")]
    [InlineData("ur-PK", "Urdu")]
    [InlineData("rm", "Romansh")]
    [InlineData("roh", "Romansh")]
    [InlineData("rm-CH", "Romansh")]
    [InlineData("ka", "Georgian")]
    [InlineData("geo", "Georgian")]
    [InlineData("kat", "Georgian")]
    [InlineData("ka-GE", "Georgian")]
    [InlineData("ga", "Irish")]
    [InlineData("gle", "Irish")]
    [InlineData("ga-IE", "Irish")]
    [InlineData("te", "Telugu")]
    [InlineData("tel", "Telugu")]
    [InlineData("te-IN", "Telugu")]
    [InlineData("af", "Afrikaans")]
    [InlineData("afr", "Afrikaans")]
    [InlineData("af-ZA", "Afrikaans")]
    [InlineData("mr", "Marathi")]
    [InlineData("mar", "Marathi")]
    [InlineData("mr-IN", "Marathi")]
    [InlineData("tl", "Tagalog")]
    [InlineData("tgl", "Tagalog")]
    [InlineData("tl-PH", "Tagalog")]
    [InlineData("mn", "Mongolian")]
    [InlineData("mon", "Mongolian")]
    [InlineData("khk", "Mongolian")]
    [InlineData("mvf", "Mongolian")]
    [InlineData("mn-Cyrl", "Mongolian")]
    [InlineData("bn", "Bengali")]
    [InlineData("ben", "Bengali")]
    [InlineData("bn-BD", "Bengali")]
    [InlineData("bn-IN", "Bengali")]
    public void FindsLanguage(string isoCode, string expected)
    {
        Assert.Equal(Language.FindByName(expected), IsoLanguages.Find(isoCode)?.Language);
    }

    [Theory]
    [InlineData("enus")]
    [InlineData("enusa")]
    [InlineData("wo")]
    [InlineData("fr-CA")]
    [InlineData("de-AU")]
    public void ReturnsNullForUnknownOrInvalidCode(string isoCode)
    {
        Assert.Null(IsoLanguages.Find(isoCode));
    }
}
