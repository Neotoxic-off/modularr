using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Cardigann;
using Modularr.Core.Indexers.Definitions;
using static Modularr.Core.Tests.Indexers.Cardigann.CardigannFixtures;

namespace Modularr.Core.Tests.Indexers.Cardigann;

// Offline searches of real public definitions of different shapes against recorded responses.
public sealed class CardigannSearcherTests
{
    [Fact]
    public async Task YtsJsonApiReturnsEveryTorrentOfTheMovie()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Fixture("yts-inception.json"));

        IReadOnlyList<ReleaseInfo> releases = Releases(await Searcher(handler).SearchAsync(
            Definition("yts.yml"), null, new CardigannSearchQuery { Type = CardigannSearchType.MovieSearch, Term = "Inception", ImdbId = 1375666 }, TestContext.Current.CancellationToken));

        Assert.Equal("https://movies-api.accel.li/api/v2/list_movies.json?query_term=tt1375666&limit=50&sort_by=date_added&order_by=desc", Assert.Single(handler.Requests).Url.AbsoluteUri);
        Assert.Equal(["Inception (2010) 720p BRRip x264 -YTS", "Inception (2010) 1080p BRRip x264 -YTS", "Inception (2010) 2160p BRRip 5.1 10Bit x265 -YTS"], releases.Select(release => release.Title));

        ReleaseInfo first = releases[0];
        Assert.Equal("https://yts.gg/torrent/download/CE9156EB497762F8B7577B71C0647A4B0C3423E1", first.Id);
        Assert.Equal(first.Id, first.DownloadUrl);
        Assert.Equal("CE9156EB497762F8B7577B71C0647A4B0C3423E1", first.InfoHash);
        Assert.StartsWith("magnet:?xt=urn:btih:", first.MagnetUrl, StringComparison.Ordinal);
        Assert.Contains("CE9156EB497762F8B7577B71C0647A4B0C3423E1", first.MagnetUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://yts.gg/movies/inception-2010", first.InfoUrl);
        Assert.Equal("YTS", first.Indexer);
        Assert.Equal(1375666, first.ImdbId);
        Assert.Equal(1148903752, first.Size);
        Assert.Equal(82, first.Seeders);
        Assert.Equal(112, first.Peers);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1446333277), first.PublishDate);
        Assert.Equal([2040, 100045], first.Categories);
        Assert.Equal(0, first.DownloadVolumeFactor);
        Assert.Equal(1, first.UploadVolumeFactor);
        Assert.Equal([2045, 100046], releases[2].Categories);
    }

    [Fact]
    public async Task ThePirateBayJsonApiReadsRootArrayAndBuildsMagnets()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Fixture("thepiratebay-ubuntu.json"));

        IReadOnlyList<ReleaseInfo> releases = Releases(await Searcher(handler).SearchAsync(
            Definition("thepiratebay.yml"), null, new CardigannSearchQuery { Term = "Ubuntu" }, TestContext.Current.CancellationToken));

        Assert.Equal("https://apibay.org/q.php?q=ubuntu&cat=", Assert.Single(handler.Requests).Url.AbsoluteUri);
        Assert.Equal(4, releases.Count);
        ReleaseInfo first = releases[0];
        Assert.Equal("Ubuntu 22.04 LTS", first.Title);
        Assert.Equal("The Pirate Bay", first.Indexer);
        Assert.Equal("2C6B6858D61DA9543D4231A71DB4B1C9264B0685", first.InfoHash);
        Assert.Null(first.DownloadUrl);
        Assert.Equal(first.MagnetUrl, first.Id);
        Assert.Contains("dn=Ubuntu", first.MagnetUrl, StringComparison.Ordinal);
        Assert.Equal("https://thepiratebay.org/description.php?id=59191690", first.InfoUrl);
        Assert.Equal(3654957056, first.Size);
        Assert.Equal(37, first.Seeders);
        Assert.Equal(42, first.Peers);
        Assert.Null(first.ImdbId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1652877231), first.PublishDate);
        Assert.Equal([4000, 100303], first.Categories);
        Assert.Equal([7020, 100601], releases[1].Categories);
    }

    [Fact]
    public async Task ThePirateBayNoResultsRowMeansNoReleases()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Ok("""[{"id":"0","name":"No results returned","info_hash":"0000000000000000000000000000000000000000","leechers":"0","seeders":"0","num_files":"0","size":"0","username":"","added":"0","status":"member","category":"0","imdb":""}]"""));

        Assert.Empty(Releases(await Searcher(handler).SearchAsync(Definition("thepiratebay.yml"), null, new CardigannSearchQuery { Term = "zzzz" }, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task X1337xHtmlTableRunsEveryPathAndParsesRows()
    {
        StubHttpHandler handler = new(request => StubHttpHandler.Fixture(request.RequestUri!.AbsolutePath.EndsWith("/1/", StringComparison.Ordinal) ? "1337x-search.html" : "1337x-empty.html"));

        IReadOnlyList<ReleaseInfo> releases = Releases(await Searcher(handler).SearchAsync(
            Definition("1337x.yml"), null, new CardigannSearchQuery { Term = "ubuntu" }, TestContext.Current.CancellationToken));

        Assert.Equal(
            ["https://1337x.to/sort-search/ubuntu/time/desc/1/", "https://1337x.to/sort-search/ubuntu/time/desc/2/", "https://1337x.to/sort-search/ubuntu/time/desc/3/", "https://1337x.to/sort-search/ubuntu/time/desc/4/"],
            handler.Requests.Select(request => request.Url.AbsoluteUri));
        Assert.Equal(
            ["Ubuntu 24.04.1 Desktop amd64 ISO", "The Ubuntu Show S01E02 1080p WEB H264-GROUP", "Ubuntu Linux Toolbox 1000 plus Commands 2nd Edition Wiley 2011 ..."],
            releases.Select(release => release.Title));

        ReleaseInfo iso = releases[0];
        Assert.Equal("https://1337x.to/torrent/6012345/Ubuntu-24-04-1-Desktop-amd64-ISO/", iso.Id);
        Assert.Equal(iso.Id, iso.DownloadUrl);
        Assert.Equal(iso.Id, iso.InfoUrl);
        Assert.Null(iso.MagnetUrl);
        Assert.Equal(6120328396, iso.Size);
        Assert.Equal(1204, iso.Seeders);
        Assert.Equal(1261, iso.Peers);
        Assert.Equal([4000, 100018], iso.Categories);
        Assert.Equal(new DateTime(DateTime.Now.Year, 9, 14, 7, 0, 0), iso.PublishDate.DateTime);

        Assert.Equal([5040, 100041], releases[1].Categories);
        Assert.Equal(DateTime.Today.AddMinutes(25), releases[1].PublishDate.DateTime);

        // No category link: the definition's default (40, Other/Misc); a year-old date with a 2-digit year.
        Assert.Equal([8010, 100040], releases[2].Categories);
        Assert.Equal(new DateTime(2011, 4, 18), releases[2].PublishDate.DateTime);
        Assert.Equal(12897484, releases[2].Size);
    }

    [Fact]
    public async Task X1337xDownloadResolvesMagnetFromDetailsPage()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Fixture("1337x-details.html"));
        Uri details = new("https://1337x.to/torrent/6012345/Ubuntu-24-04-1-Desktop-amd64-ISO/");

        CardigannDownloadResult result = await Searcher(handler).ResolveDownloadAsync(Definition("1337x.yml"), null, details, TestContext.Current.CancellationToken);

        Uri link = Assert.IsType<CardigannDownloadResult.Resolved>(result).Link;
        Assert.StartsWith("magnet:?xt=urn:btih:C8295CE630F2064F08440DB1534E4992CFE4862A&dn=Ubuntu", link.OriginalString, StringComparison.Ordinal);
        Assert.Equal(details, Assert.Single(handler.Requests).Url);
    }

    [Theory]
    [InlineData("<html><body>Torrent not found</body></html>", "magnet:?xt=urn:btih:C8295CE630F2064F08440DB1534E4992CFE4862A")]
    [InlineData("d8:announce35:udp://tracker.opentrackr.org:1337e", "https://itorrents.net/torrent/C8295CE630F2064F08440DB1534E4992CFE4862A.torrent")]
    public async Task X1337xDownloadFallsBackWhenTheTorrentMirrorServesHtml(string mirrorBody, string expectedPrefix)
    {
        StubHttpHandler handler = new(request => request.RequestUri!.Host == "itorrents.net" ? StubHttpHandler.Ok(mirrorBody) : StubHttpHandler.Fixture("1337x-details.html"));
        CardigannDefinition definition = Definition("1337x.yml");
        definition = definition with
        {
            Settings = [.. definition.Settings.Select(setting => setting.Name switch
            {
                "primarydownloadlink" => setting with { Default = "//itorrents." },
                "fallbackdownloadlink" => setting with { Default = "magnet:" },
                _ => setting,
            })],
        };

        CardigannDownloadResult result = await Searcher(handler).ResolveDownloadAsync(definition, null, new Uri("https://1337x.to/torrent/6012345/x/"), TestContext.Current.CancellationToken);

        Assert.StartsWith(expectedPrefix, Assert.IsType<CardigannDownloadResult.Resolved>(result).Link.OriginalString, StringComparison.Ordinal);
        Assert.Contains(handler.Requests, request => request.Url.AbsoluteUri == "https://itorrents.net/torrent/C8295CE630F2064F08440DB1534E4992CFE4862A.torrent");
    }

    [Fact]
    public async Task EztvTvSearchSendsHeadersAndKeepsOnlyReleasesMatchingTheQuery()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Fixture("eztv-search.html"));

        IReadOnlyList<ReleaseInfo> releases = Releases(await Searcher(handler).SearchAsync(
            Definition("eztv.yml"), null, new CardigannSearchQuery { Type = CardigannSearchType.TvSearch, Term = "The Last of Us", Season = 2, Episode = "1" }, TestContext.Current.CancellationToken));

        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal("https://eztvx.to/search/The-Last-of-Us-S02E01", request.Url.AbsoluteUri);
        Assert.StartsWith("sort_no=100;", request.Headers["Cookie"], StringComparison.Ordinal);

        // "Us S01E01" only matches one of the two search terms (andmatch).
        Assert.Equal(["The Last of Us S02E01 1080p WEB H264-SuccessfulCrab", "The Last of Us S02E01 720p WEB H264-SuccessfulCrab"], releases.Select(release => release.Title));
        ReleaseInfo first = releases[0];
        Assert.StartsWith("magnet:?xt=urn:btih:4e2c0b1a0f5d1e6f7a8b9c0d1e2f3a4b5c6d7e8f", first.MagnetUrl, StringComparison.Ordinal);
        Assert.Equal(first.MagnetUrl, first.Id);
        Assert.Equal("4e2c0b1a0f5d1e6f7a8b9c0d1e2f3a4b5c6d7e8f", first.InfoHash);
        Assert.Equal("https://eztvx.to/ep/2163512/the-last-of-us-s02e01-1080p-web-h264-successfulcrab/", first.InfoUrl);
        Assert.Equal(3457448673, first.Size);
        Assert.Equal(1523, first.Seeders);
        Assert.Equal([5000], first.Categories);
        Assert.InRange(first.PublishDate, DateTimeOffset.Now.AddHours(-2.25).AddMinutes(-1), DateTimeOffset.Now.AddHours(-2.25).AddMinutes(1));
    }

    [Fact]
    public async Task NyaaAnimeSearchDeduplicatesPathsAndPrefersMagnets()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Fixture("nyaasi-search.html"));

        IReadOnlyList<ReleaseInfo> releases = Releases(await Searcher(handler).SearchAsync(
            Definition("nyaasi.yml"), null, new CardigannSearchQuery { Term = "frieren" }, TestContext.Current.CancellationToken));

        Assert.Equal("https://nyaa.si/?f=0&c=0_0&s=id&o=desc&q=frieren", Assert.Single(handler.Requests).Url.AbsoluteUri);
        Assert.Equal(2, releases.Count);
        ReleaseInfo first = releases[0];
        Assert.Equal("[SubsPlease] Sousou no Frieren - 28 (1080p) [7A1B2C3D].mkv", first.Title);
        Assert.Null(first.DownloadUrl);
        Assert.StartsWith("magnet:?xt=urn:btih:5c3f8e1d2a9b7c6e4f0a1b2c3d4e5f6a7b8c9d0e", first.MagnetUrl, StringComparison.Ordinal);
        Assert.Equal("5c3f8e1d2a9b7c6e4f0a1b2c3d4e5f6a7b8c9d0e", first.InfoHash);
        Assert.Equal("https://nyaa.si/view/1765432", first.InfoUrl);
        Assert.Equal(1503238553, first.Size);
        Assert.Equal(new DateTime(2024, 3, 23, 22, 56, 0, DateTimeKind.Utc), first.PublishDate.UtcDateTime);
        Assert.Equal(2471, first.Seeders);
        Assert.Equal(2509, first.Peers);
        Assert.Equal(41536, first.Grabs);
        Assert.Contains(5070, first.Categories);
        Assert.Contains(2020, first.Categories);
        Assert.Equal("[Erai-raws] Sousou no Frieren - 01 ~ 28 [1080p][Multiple Subtitle]", releases[1].Title);
    }

    [Fact]
    public async Task ShowRssXmlFeedUsesCaseSelectorsAndAndmatch()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Fixture("showrss-all.xml"));

        IReadOnlyList<ReleaseInfo> releases = Releases(await Searcher(handler).SearchAsync(
            Definition("showrss.yml"), null, new CardigannSearchQuery { Term = "Young Restless" }, TestContext.Current.CancellationToken));

        Assert.Equal("https://showrss.info/other/all.rss", Assert.Single(handler.Requests).Url.AbsoluteUri);
        Assert.Equal(["The Young and the Restless S54E10 1080p WEB h264 DiRT", "The Young and the Restless S54E10 720p WEB h264 DiRT"], releases.Select(release => release.Title));
        ReleaseInfo first = releases[0];
        Assert.Equal("2F75130B0ADA0DACCC9AB18A74407CECA1BC6EFB", first.InfoHash);
        Assert.StartsWith("magnet:?xt=urn:btih:2F75130B0ADA0DACCC9AB18A74407CECA1BC6EFB", first.MagnetUrl, StringComparison.Ordinal);
        Assert.Equal("https://showrss.info/", first.InfoUrl);
        Assert.Equal([5040], first.Categories);
        Assert.Equal(536870912, first.Size);
        Assert.Equal(new DateTime(2026, 10, 22, 15, 5, 7), first.PublishDate.DateTime);
    }

    [Fact]
    public async Task RssSearchWithoutKeywordsSkipsAndmatch()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Fixture("showrss-all.xml"));

        Assert.Equal(4, Releases(await Searcher(handler).SearchAsync(Definition("showrss.yml"), null, new CardigannSearchQuery(), TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task ConfiguredBaseUrlReplacesThePrimaryLinkUnlessLegacy()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Fixture("eztv-search.html"));
        CardigannSearcher searcher = Searcher(handler);
        CardigannDefinition definition = Definition("eztv.yml");

        await searcher.SearchAsync(definition, "https://eztv.mirror.example", new CardigannSearchQuery(), TestContext.Current.CancellationToken);
        await searcher.SearchAsync(definition, definition.LegacyLinks[0], new CardigannSearchQuery(), TestContext.Current.CancellationToken);

        Assert.Equal(["https://eztv.mirror.example/home", "https://eztvx.to/home"], handler.Requests.Select(request => request.Url.AbsoluteUri));
    }
}
