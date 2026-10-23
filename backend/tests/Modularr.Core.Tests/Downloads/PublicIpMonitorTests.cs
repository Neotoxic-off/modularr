using System.Text.Json;
using Modularr.Core.Downloads;

namespace Modularr.Core.Tests.Downloads;

public sealed class PublicIpMonitorTests
{
    [Fact]
    public void ParsesIpOrgAndCountryAndDropsTheAsNumber()
    {
        PublicIpCheck check = PublicIpMonitor.ParseInfo("""{"ip":"185.65.134.1","city":"Amsterdam","country":"NL","org":"AS39351 31173 Services AB"}""", DateTimeOffset.UnixEpoch);

        Assert.Equal(new PublicIpCheck("185.65.134.1", "31173 Services AB", "NL", DateTimeOffset.UnixEpoch, null), check);
    }

    [Fact]
    public void OrgAndCountryAreOptional()
    {
        PublicIpCheck check = PublicIpMonitor.ParseInfo("""{"ip":"2001:4860:4860::8888"}""", DateTimeOffset.UnixEpoch);

        Assert.Equal("2001:4860:4860::8888", check.PublicIp);
        Assert.Null(check.Org);
        Assert.Null(check.Country);
    }

    [Theory]
    [InlineData("""{"ip":"<script>"}""")]
    [InlineData("""{"error":{"title":"Rate limit exceeded"}}""")]
    [InlineData("""["1.2.3.4"]""")]
    public void RefusesAnAnswerWithoutAnAddress(string json) =>
        Assert.Throws<FormatException>(() => PublicIpMonitor.ParseInfo(json, DateTimeOffset.UnixEpoch));

    [Fact]
    public void RefusesInvalidJson() =>
        Assert.ThrowsAny<JsonException>(() => PublicIpMonitor.ParseInfo("<html>", DateTimeOffset.UnixEpoch));
}
