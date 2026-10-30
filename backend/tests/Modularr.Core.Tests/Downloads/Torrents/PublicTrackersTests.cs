using System.Net;
using Modularr.Core.Downloads;
using Modularr.Core.Downloads.Torrents;

namespace Modularr.Core.Tests.Downloads.Torrents;

public sealed class PublicTrackersTests
{
    [Fact]
    public void OnlyPublicUdpAndHttpTrackersAreKept()
    {
        const string list = """
            udp://tracker.opentrackr.org:1337/announce

            http://tracker.example.org:80/announce
            udp://tracker.opentrackr.org:1337/announce
            udp://127.0.0.1:6969/announce
            http://192.168.1.10/announce
            http://169.254.169.254/latest/meta-data
            udp://[::1]:6969/announce
            http://localhost:8080/announce
            http://router.local/announce
            wss://tracker.example.org/announce
            ftp://tracker.example.org/announce
            http://user:pass@tracker.example.org/announce
            not a url
            """;

        Assert.Equal(
            ["udp://tracker.opentrackr.org:1337/announce", "http://tracker.example.org/announce"],
            PublicTrackers.Parse(list).Select(u => u.AbsoluteUri));
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("10.0.0.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("100.64.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::ffff:192.168.0.1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("fe80::1", false)]
    public void PrivateAndReservedAddressesAreNotPublic(string address, bool expected) =>
        Assert.Equal(expected, PublicAddress.IsPublic(IPAddress.Parse(address)));
}
