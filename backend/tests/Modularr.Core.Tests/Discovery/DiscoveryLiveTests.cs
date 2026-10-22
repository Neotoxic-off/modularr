using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Discovery;
using Modularr.Core.Metadata;
using Modularr.Core.Tests.Persistence;

namespace Modularr.Core.Tests.Discovery;

// Every discover list and a mixed lookup against the real keyless sources. Opt in with MODULARR_LIVE_METADATA=1.
public sealed class DiscoveryLiveTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose()
    {
        _cache.Dispose();
        _database.Dispose();
    }

    public static TheoryData<string> Lists => ["movies:Popular", "movies:Trending", "movies:Upcoming", "series:Popular", "series:Trending", "series:Airing", "anime:Trending", "anime:Season"];

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task ListAnswersWithoutAKey(string list)
    {
        DiscoveryService discovery = Service();
        CancellationToken ct = TestContext.Current.CancellationToken;
        string name = list.Split(':')[1];

        DiscoveryResult result = list.Split(':')[0] switch
        {
            "movies" => await discovery.DiscoverMoviesAsync(Enum.Parse<MovieList>(name), ct),
            "series" => await discovery.DiscoverSeriesAsync(Enum.Parse<SeriesList>(name), ct),
            _ => await discovery.DiscoverAnimeAsync(Enum.Parse<AnimeList>(name), ct),
        };

        Assert.Null(result.Error);
        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, i => Assert.True(i.TmdbId > 0 || i.TvdbId > 0));
    }

    [Fact]
    public async Task LookupFindsMoviesAndSeries()
    {
        IReadOnlyList<DiscoveryItem> items = await Service().LookupAsync("dune 2021", includeMovies: true, includeSeries: true, TestContext.Current.CancellationToken);

        Assert.Equal((438631, 2021), (items[0].TmdbId!.Value, items[0].Year!.Value));
        Assert.Contains(items, i => i.TvdbId is not null);
    }

    private DiscoveryService Service()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("MODULARR_LIVE_METADATA") == "1", "Set MODULARR_LIVE_METADATA=1 to query the live metadata sources.");
        IOptions<ModularrOptions> options = Options.Create(new ModularrOptions());
        HttpClient client = new();
        return new DiscoveryService(new MovieMetadataClient(client, options), new SeriesMetadataClient(client, options),
            new ShowDiscoveryClient(client, options), _cache, _database.NewContext());
    }
}
