// Cases from the Radarr CrapParserFixture (GPL-3.0).
using System.Security.Cryptography;
using System.Text;
using Modularr.Core.Parsing.Movies;

namespace Modularr.Core.Tests.Parsing.Movies;

public sealed class MovieCrapParserTests
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
    public void DoesNotParseCrap(string title)
    {
        Assert.Null(MovieTitleParser.ParseMovieTitle(title));
    }

    [Fact]
    public void DoesNotParseMd5()
    {
        string hash = "CRAPPY TEST SEED";

        for (int i = 0; i < 100; i++)
        {
#pragma warning disable CA5351 // MD5 is the point: indexers obfuscate names with md5 hashes.
            hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(hash)));
#pragma warning restore CA5351
            Assert.Null(MovieTitleParser.ParseMovieTitle(hash));
        }
    }

    [Theory]
    [InlineData(32)]
    [InlineData(40)]
    public void DoesNotParseRandom(int length)
    {
        const string Charset = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

        for (int i = 0; i < 500; i++)
        {
            string hash = new([.. Enumerable.Range(0, length).Select(_ => Charset[Random.Shared.Next(Charset.Length)])]);
            Assert.Null(MovieTitleParser.ParseMovieTitle(hash));
        }
    }

    [Theory]
    [InlineData("thebiggestmovie1618finale")]
    public void DoesNotParseFileNameWithoutProperSpacing(string fileName)
    {
        Assert.Null(MovieTitleParser.ParseMovieTitle(fileName));
    }

    [Theory]
    [InlineData("Big Movie (S01E18) Complete 360p HDTV AAC H.264-NEXT")]
    public void DoesNotParseInvalidReleaseName(string fileName)
    {
        Assert.Null(MovieTitleParser.ParseMovieTitle(fileName));
    }
}
