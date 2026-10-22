// Ported from Prowlarr NzbDrone.Core.Test/ParserTests/ParseUtilFixture.cs (should_normalize_multiple_spaces skipped: NormalizeMultiSpaces is not used by Cardigann).
using Modularr.Core.Indexers.Cardigann;

namespace Modularr.Core.Tests.Indexers.Cardigann;

public sealed class ParseUtilTests
{
    [Theory]
    [InlineData("1023.4 KB", 1047961L)]
    [InlineData("1023.4 MB", 1073112678L)]
    [InlineData("1,023.4 MB", 1073112678L)]
    [InlineData("1.023,4 MB", 1073112678L)]
    [InlineData("1 023,4 MB", 1073112678L)]
    [InlineData("1.023.4 MB", 1073112678L)]
    [InlineData("1023.4 GB", 1098867382681L)]
    [InlineData("1023.4 TB", 1125240199865958L)]
    public void ParsesSize(string stringSize, long size)
    {
        Assert.Equal(size, ParseUtil.GetBytes(stringSize));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("11", 11)]
    [InlineData("1000 grabs", 1000)]
    [InlineData("2.222", 2222)]
    [InlineData("2,222", 2222)]
    [InlineData("2 222", 2222)]
    [InlineData("2,22", 222)]
    public void ParsesIntFromString(string original, int parsedInt)
    {
        Assert.Equal(parsedInt, ParseUtil.CoerceInt(original));
    }

    [Theory]
    [InlineData("1.0", 1.0)]
    [InlineData("1.1", 1.1)]
    [InlineData("1000 grabs", 1000.0)]
    [InlineData("2.222", 2.222)]
    [InlineData("2,222", 2.222)]
    [InlineData("2.222,22", 2222.22)]
    [InlineData("2,222.22", 2222.22)]
    [InlineData("2 222", 2222.0)]
    [InlineData("2,22", 2.22)]
    public void ParsesDoubleFromString(string original, double parsedDouble)
    {
        Assert.Equal(parsedDouble, ParseUtil.CoerceDouble(original));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("1", 1L)]
    [InlineData("1000 grabs", 1000L)]
    [InlineData("asdf123asdf", 123L)]
    [InlineData("asdf123asdf456asdf", 123L)]
    public void ParsesLongFromString(string? original, long? parsedLong)
    {
        Assert.Equal(parsedLong, ParseUtil.GetLongFromString(original));
    }

    [Theory]
    [InlineData("tt0183790", "tt0183790")]
    [InlineData("0183790", "tt0183790")]
    [InlineData("183790", "tt0183790")]
    [InlineData("tt10001870", "tt10001870")]
    [InlineData("10001870", "tt10001870")]
    [InlineData("tt", null)]
    [InlineData("tt0", null)]
    [InlineData("abc", null)]
    [InlineData("abc0", null)]
    [InlineData("0", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParsesFullImdbIdFromString(string? input, string? expected)
    {
        Assert.Equal(expected, ParseUtil.GetFullImdbId(input));
    }
}
