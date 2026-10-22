using Modularr.Core.Downloads;

namespace Modularr.Core.Tests.Downloads;

public sealed class DownloadServiceTests
{
    private static readonly string[] Sites = ["https://www.yts.example/", "https://yts.mirror.example/"];

    [Theory]
    [InlineData("https://yts.example/torrent/download/ABC", true)]
    [InlineData("https://www.yts.example/torrent/download/ABC", true)]
    [InlineData("http://dl.yts.example/t.torrent", true)]
    [InlineData("https://yts.mirror.example/t.torrent", true)]
    [InlineData("http://169.254.169.254/latest/meta-data/", false)]
    [InlineData("http://localhost:8080/api/series", false)]
    [InlineData("https://evilyts.example/t.torrent", false)]
    [InlineData("https://yts.example.evil.test/t.torrent", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("ftp://yts.example/t.torrent", false)]
    public void OnlyFetchesLinksOnTheIndexerSites(string link, bool expected)
    {
        Assert.Equal(expected, DownloadService.IsIndexerLink(new Uri(link), Sites));
    }
}
