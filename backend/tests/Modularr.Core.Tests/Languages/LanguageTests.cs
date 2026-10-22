// Cases from the Radarr LanguageFixture (GPL-3.0), plus the Sonarr-only languages.
using Modularr.Core.Languages;

namespace Modularr.Core.Tests.Languages;

public sealed class LanguageTests
{
    [Theory]
    [InlineData(-2, "Original")]
    [InlineData(-1, "Any")]
    [InlineData(0, "Unknown")]
    [InlineData(1, "English")]
    [InlineData(2, "French")]
    [InlineData(3, "Spanish")]
    [InlineData(4, "German")]
    [InlineData(5, "Italian")]
    [InlineData(6, "Danish")]
    [InlineData(7, "Dutch")]
    [InlineData(8, "Japanese")]
    [InlineData(9, "Icelandic")]
    [InlineData(10, "Chinese")]
    [InlineData(11, "Russian")]
    [InlineData(12, "Polish")]
    [InlineData(13, "Vietnamese")]
    [InlineData(14, "Swedish")]
    [InlineData(15, "Norwegian")]
    [InlineData(16, "Finnish")]
    [InlineData(17, "Turkish")]
    [InlineData(18, "Portuguese")]
    [InlineData(19, "Flemish")]
    [InlineData(20, "Greek")]
    [InlineData(21, "Korean")]
    [InlineData(22, "Hungarian")]
    [InlineData(23, "Hebrew")]
    [InlineData(24, "Lithuanian")]
    [InlineData(25, "Czech")]
    [InlineData(26, "Hindi")]
    [InlineData(27, "Romanian")]
    [InlineData(28, "Thai")]
    [InlineData(29, "Bulgarian")]
    [InlineData(30, "Portuguese (Brazil)")]
    [InlineData(31, "Arabic")]
    [InlineData(32, "Ukrainian")]
    [InlineData(33, "Persian")]
    [InlineData(34, "Bengali")]
    [InlineData(35, "Slovak")]
    [InlineData(36, "Latvian")]
    [InlineData(37, "Spanish (Latino)")]
    [InlineData(38, "Catalan")]
    [InlineData(39, "Croatian")]
    [InlineData(40, "Serbian")]
    [InlineData(41, "Bosnian")]
    [InlineData(42, "Estonian")]
    [InlineData(43, "Tamil")]
    [InlineData(44, "Indonesian")]
    [InlineData(45, "Telugu")]
    [InlineData(46, "Macedonian")]
    [InlineData(47, "Slovenian")]
    [InlineData(48, "Malayalam")]
    [InlineData(49, "Kannada")]
    [InlineData(50, "Albanian")]
    [InlineData(51, "Afrikaans")]
    [InlineData(52, "Marathi")]
    [InlineData(53, "Tagalog")]
    [InlineData(54, "Urdu")]
    [InlineData(55, "Romansh")]
    [InlineData(56, "Mongolian")]
    [InlineData(57, "Georgian")]
    // Sonarr expected 47: ids follow Radarr's larger set, Sonarr-only languages are appended (fresh DB).
    [InlineData(58, "Azerbaijani")]
    // Sonarr expected 48: ids follow Radarr's larger set, Sonarr-only languages are appended (fresh DB).
    [InlineData(59, "Uzbek")]
    // Sonarr expected 49: ids follow Radarr's larger set, Sonarr-only languages are appended (fresh DB).
    [InlineData(60, "Malay")]
    // Sonarr expected 53: ids follow Radarr's larger set, Sonarr-only languages are appended (fresh DB).
    [InlineData(61, "Irish")]
    public void ConvertsBetweenIdAndLanguage(int id, string name)
    {
        Assert.Equal(name, Language.FindById(id).Name);
        Assert.Equal(id, Language.FindByName(name).Id);
    }

    [Fact]
    public void RejectsUnknownId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Language.FindById(999));
    }
}