// Behavior from Prowlarr IndexerBase.GetDefaultBaseUrl (legacy link migration) and Cardigann "getUrls" (domain choices).
using Modularr.Core.Indexers.Definitions;

namespace Modularr.Core.Tests.Indexers.Definitions;

public sealed class IndexerDomainTests
{
    private static readonly CardigannDefinition Definition = CardigannDefinitionLoader.Load(CardigannDefinitionLoaderTests.Minimal);

    [Theory]
    [InlineData(null, "https://test.example/")]
    [InlineData("", "https://test.example/")]
    [InlineData("   ", "https://test.example/")]
    [InlineData("https://old.example/", "https://test.example/")]
    [InlineData("https://test.example/", "https://test.example/")]
    [InlineData("https://mirror.example/", "https://mirror.example/")]
    [InlineData("https://my-proxy.example/", "https://my-proxy.example/")]
    public void ResolvesBaseUrl(string? configured, string expected)
    {
        Assert.Equal(expected, Definition.ResolveBaseUrl(configured));
    }

    // Prowlarr expected these to stay unchanged: it compares legacy links with exact string equality.
    [Theory]
    [InlineData("https://old.example")]
    [InlineData("https://OLD.example/")]
    public void MigratesLegacyLinkWhateverTrailingSlashOrCase(string configured)
    {
        Assert.Equal("https://test.example/", Definition.ResolveBaseUrl(configured));
    }

    [Theory]
    [InlineData(null, new[] { "https://test.example/", "https://mirror.example/" })]
    [InlineData("https://old.example/", new[] { "https://test.example/", "https://mirror.example/" })]
    [InlineData("https://mirror.example/", new[] { "https://mirror.example/", "https://test.example/" })]
    [InlineData("https://mirror.example", new[] { "https://mirror.example", "https://test.example/" })]
    [InlineData("https://my-proxy.example/", new[] { "https://my-proxy.example/", "https://test.example/", "https://mirror.example/" })]
    public void ListsFailoverCandidatesActiveFirst(string? configured, string[] expected)
    {
        Assert.Equal(expected, Definition.CandidateBaseUrls(configured));
    }

    [Fact]
    public void MigratesEveryLegacyLinkOfARealDefinition()
    {
        CardigannDefinition definition = CardigannDefinitionLoaderTests.LoadFixture("1337x.yml");

        Assert.All(definition.LegacyLinks, legacy => Assert.Equal("https://1337x.to/", definition.ResolveBaseUrl(legacy)));
        Assert.Equal("https://x1337x.eu/", definition.ResolveBaseUrl("https://x1337x.eu/"));
        Assert.Equal(definition.Links.Count, definition.CandidateBaseUrls("https://1337x.so/").Count);
    }

    [Fact]
    public void UpgradesHttpLegacyLinkToHttps()
    {
        CardigannDefinition definition = CardigannDefinitionLoaderTests.LoadFixture("traht.yml");

        Assert.True(definition.IsLegacyLink("http://traht.org/"));
        Assert.Equal("https://traht.org/", definition.ResolveBaseUrl("http://traht.org/"));
    }
}
