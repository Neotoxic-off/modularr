using Modularr.Core.Parsing.Episodes;

namespace Modularr.Core.Tests.Parsing.Episodes;

// Cases from the Sonarr CrapParserFixture (GPL-3.0).
public sealed class CrapParserTests
{
    [Theory]
    [InlineData("76El6LcgLzqb426WoVFg1vVVVGx4uCYopQkfjmLe")]
    [InlineData("Vrq6e1Aba3U amCjuEgV5R2QvdsLEGYF3YQAQkw8")]
    [InlineData("TDAsqTea7k4o6iofVx3MQGuDK116FSjPobMuh8oB")]
    [InlineData("yp4nFodAAzoeoRc467HRh1mzuT17qeekmuJ3zFnL")]
    [InlineData("oxXo8S2272KE1 lfppvxo3iwEJBrBmhlQVK1gqGc")]
    [InlineData("dPBAtu681Ycy3A4NpJDH6kNVQooLxqtnsW1Umfiv")]
    [InlineData("password - \"bdc435cb-93c4-4902-97ea-ca00568c3887.337\" yEnc")]
    [InlineData("185d86a343e39f3341e35c4dad3f9959")]
    [InlineData("ba27283b17c00d01193eacc02a8ba98eeb523a76")]
    [InlineData("45a55debe3856da318cc35882ad07e43cd32fd15")]
    [InlineData("86420f8ee425340d8894bf3bc636b66404b95f18")]
    [InlineData("ce39afb7da6cf7c04eba3090f0a309f609883862")]
    [InlineData("THIS SHOULD NEVER PARSE")]
    [InlineData("Vh1FvU3bJXw6zs8EEUX4bMo5vbbMdHghxHirc.mkv")]
    [InlineData("0e895c37245186812cb08aab1529cf8ee389dd05.mkv")]
    [InlineData("08bbc153931ce3ca5fcafe1b92d3297285feb061.mkv")]
    [InlineData("185d86a343e39f3341e35c4dad3ff159")]
    [InlineData("ah63jka93jf0jh26ahjas961.mkv")]
    [InlineData("qrdSD3rYzWb7cPdVIGSn4E7")]
    [InlineData("QZC4HDl7ncmzyUj9amucWe1ddKU1oFMZDd8r0dEDUsTd")]
    [InlineData("abc.xyz.af6021c37f7852")]
    [InlineData("e096aeb3c2c0483a96f5b32fc6d10ff5")]
    [InlineData("_unpack e096aeb3c2c0483a96f5b32fc6d10ff5.mkv")]
    public void ShouldNotParseCrap(string title)
    {
        Assert.Null(EpisodeParser.ParseTitle(title));
    }

    [Theory]
    [InlineData("علم نف) أ.دعادل الأبيض ٢٠٢٤ ٣ ٣")]
    [InlineData("ror-240618_1007-1022-")]
    public void ShouldParseUnknownFormatsWithoutError(string title)
    {
        Assert.NotNull(EpisodeParser.ParseTitle(title));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(40)]
    public void ShouldNotParseRandom(int length)
    {
        const string charset = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

        for (int i = 0; i < 500; i++)
        {
            string hash = string.Concat(Enumerable.Range(0, length).Select(_ => charset[Random.Shared.Next(charset.Length)]));
            Assert.Null(EpisodeParser.ParseTitle(hash));
        }
    }

    [Theory]
    [InlineData("theseriestitle1618finale")]
    public void ShouldNotParseFileNameWithoutProperSpacing(string fileName)
    {
        Assert.Null(EpisodeParser.ParseTitle(fileName));
    }

    [Theory]
    [InlineData("Series Title (2018) Complete 360p HDTV AAC H.264-NEXT")]
    public void ShouldNotParseInvalidReleaseName(string fileName)
    {
        Assert.Null(EpisodeParser.ParseTitle(fileName));
    }

    [Theory]
    [InlineData("Specials/Series - Episode Title (part 1)")]
    public void ShouldNotParseSpecialWithPartNumber(string fileName)
    {
        Assert.Null(EpisodeParser.ParseTitle(fileName));
    }
}
