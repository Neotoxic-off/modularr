using System.Collections.Frozen;
using System.Net;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Discovery;
using Modularr.Core.Metadata;
using Modularr.Core.Tests.Metadata;

namespace Modularr.Core.Tests.Discovery;

// Recorded TVmaze, AniList and anime-lists answers (the AniList fixture adds a sequel known only through its prequel, and an unmapped show).
public sealed class ShowDiscoveryClientTests
{
    private const string TvMaze = "https://api.tvmaze.com/";

    private readonly StubHttpHandler _http = new StubHttpHandler()
        .Fixture(TvMaze + "schedule?country=US", "tvmaze-schedule-us.json")
        .Fixture(TvMaze + "schedule/web", "tvmaze-schedule-web.json")
        .Fixture("https://graphql.anilist.co/", "anilist-trending.json")
        .Fixture("https://raw.githubusercontent.com/Fribb/anime-lists/", "anime-mapping.json");

    private ShowDiscoveryClient Client() => new(new HttpClient(_http), Options.Create(new ModularrOptions()));

    [Fact]
    public async Task AiringMergesBroadcastAndWebSchedulesMostFollowedFirst()
    {
        IReadOnlyList<DiscoveryItem> shows = await Client().GetAiringShowsAsync(TestContext.Current.CancellationToken);

        // News and shows without a TVDB id are left out; a show airing twice is listed once.
        Assert.Equal([70328, 79838, 434200, 412558, 482784], shows.Select(s => s.TvdbId!.Value));
        DiscoveryItem young = shows[0];
        Assert.Equal(("The Young and the Restless", 1973, 5.7m, "Continuing", MediaType.Series), (young.Title, young.Year, young.Rating, young.Status, young.Kind));
        Assert.StartsWith("https://static.tvmaze.com/", young.Poster, StringComparison.Ordinal);
        Assert.DoesNotContain('<', young.Overview!);
        Assert.Equal(MediaType.Anime, shows.Single(s => s.TvdbId == 434200).Kind);
    }

    [Fact]
    public async Task AnimeIsMappedToTvdbThroughTheMappingOrAPrequel()
    {
        ShowDiscoveryClient client = Client();
        FrozenDictionary<int, int> mapping = await client.GetAnimeMappingAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<DiscoveryItem> anime = await client.GetTrendingAnimeAsync(mapping, TestContext.Current.CancellationToken);

        Assert.Equal(4, mapping.Count);
        Assert.Equal([384541, 453028, 459627, 123456], anime.Select(a => a.TvdbId!.Value));
        DiscoveryItem cyberpunk = anime[0];
        Assert.Equal(("Cyberpunk: Edgerunners 2", 2026, 8.5m, MediaType.Anime), (cyberpunk.Title, cyberpunk.Year, cyberpunk.Rating, cyberpunk.Kind));
        Assert.Equal("Anime", cyberpunk.Genres[0]);
        Assert.NotNull(cyberpunk.Backdrop);
        Assert.DoesNotContain("<br>", cyberpunk.Overview!, StringComparison.Ordinal);
        Assert.Equal("Sequel Only Known Through Its Prequel", anime[3].Title);
        Assert.Contains("\"sort\":\"TRENDING_DESC\"", LastBody(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SeasonAskForTheCurrentBroadcastSeason()
    {
        await Client().GetSeasonAnimeAsync(FrozenDictionary<int, int>.Empty, new DateOnly(2026, 10, 22), TestContext.Current.CancellationToken);

        string body = LastBody();
        Assert.Contains("\"season\":\"FALL\"", body, StringComparison.Ordinal);
        Assert.Contains("\"year\":2026", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreachableSourceIsAMetadataSourceError()
    {
        StubHttpHandler down = new StubHttpHandler().Respond(TvMaze, HttpStatusCode.ServiceUnavailable);
        ShowDiscoveryClient client = new(new HttpClient(down), Options.Create(new ModularrOptions()));

        await Assert.ThrowsAsync<MetadataSourceException>(() => client.GetPopularShowsAsync(TestContext.Current.CancellationToken));
    }

    private string LastBody() => _http.Bodies[^1];
}
