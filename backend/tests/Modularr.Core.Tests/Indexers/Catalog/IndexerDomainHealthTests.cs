using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Catalog;
using Modularr.Core.Indexers.Definitions;
using Modularr.Core.Tests.Indexers.Definitions;

namespace Modularr.Core.Tests.Indexers.Catalog;

public sealed class IndexerDomainHealthTests
{
    // Links: test.example (primary), mirror.example; legacy: old.example.
    private static readonly CardigannDefinition Definition = CardigannDefinitionLoader.Load(CardigannDefinitionLoaderTests.Minimal);

    private static IndexerDomainHealth Health(Dictionary<string, string>? baseUrls = null) =>
        new(Options.Create(new ModularrOptions { Indexers = new IndexerOptions { BaseUrls = baseUrls ?? [] } }));

    private static void Fail(IndexerDomainHealth health, int times)
    {
        for (int i = 0; i < times; i++)
        {
            health.ReportFailure(Definition, health.ActiveBaseUrl(Definition));
        }
    }

    [Fact]
    public void StartsOnTheFirstLink()
    {
        Assert.Equal("https://test.example/", Health().ActiveBaseUrl(Definition));
    }

    [Theory]
    [InlineData("https://my-proxy.example/", "https://my-proxy.example/")]
    [InlineData("https://old.example/", "https://test.example/")]
    public void StartsOnTheConfiguredBaseUrlMigratingALegacyOne(string configured, string expected)
    {
        Assert.Equal(expected, Health(new() { ["test"] = configured }).ActiveBaseUrl(Definition));
    }

    [Fact]
    public void FailsOverAfterConsecutiveFailures()
    {
        IndexerDomainHealth health = Health();

        Fail(health, IndexerDomainHealth.FailoverThreshold - 1);
        Assert.Equal("https://test.example/", health.ActiveBaseUrl(Definition));

        Fail(health, 1);
        Assert.Equal("https://mirror.example/", health.ActiveBaseUrl(Definition));
    }

    [Fact]
    public void SuccessResetsTheFailureCount()
    {
        IndexerDomainHealth health = Health();

        Fail(health, IndexerDomainHealth.FailoverThreshold - 1);
        health.ReportSuccess(Definition, "https://test.example/");
        Fail(health, IndexerDomainHealth.FailoverThreshold - 1);

        Assert.Equal("https://test.example/", health.ActiveBaseUrl(Definition));
    }

    [Fact]
    public void WrapsAroundAfterTheLastLink()
    {
        IndexerDomainHealth health = Health(new() { ["test"] = "https://my-proxy.example/" });

        Fail(health, IndexerDomainHealth.FailoverThreshold);
        Assert.Equal("https://test.example/", health.ActiveBaseUrl(Definition));
        Fail(health, IndexerDomainHealth.FailoverThreshold);
        Assert.Equal("https://mirror.example/", health.ActiveBaseUrl(Definition));
        Fail(health, IndexerDomainHealth.FailoverThreshold);
        Assert.Equal("https://my-proxy.example/", health.ActiveBaseUrl(Definition));
    }

    [Fact]
    public void CoolsDownOnceEveryMirrorFailedAndASuccessEndsIt()
    {
        IndexerDomainHealth health = Health();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Fail(health, IndexerDomainHealth.FailoverThreshold); // primary down, still a mirror to try
        Assert.False(health.IsCoolingDown(Definition, now));

        Fail(health, IndexerDomainHealth.FailoverThreshold); // the mirror too: every link failed
        Assert.True(health.IsCoolingDown(Definition, now));
        Assert.False(health.IsCoolingDown(Definition, now + IndexerDomainHealth.Cooldown + TimeSpan.FromMinutes(1)));

        health.ReportSuccess(Definition, health.ActiveBaseUrl(Definition));
        Assert.False(health.IsCoolingDown(Definition, now));
    }

    [Fact]
    public void IgnoresFailuresOfADomainNoLongerActive()
    {
        IndexerDomainHealth health = Health();
        Fail(health, IndexerDomainHealth.FailoverThreshold);

        for (int i = 0; i < IndexerDomainHealth.FailoverThreshold; i++)
        {
            health.ReportFailure(Definition, "https://test.example/");
        }

        Assert.Equal("https://mirror.example/", health.ActiveBaseUrl(Definition));
    }

    [Fact]
    public void RestartsOnTheFirstLinkWhenAnUpdatedDefinitionDropsTheActiveOne()
    {
        IndexerDomainHealth health = Health();
        Fail(health, IndexerDomainHealth.FailoverThreshold);

        CardigannDefinition updated = Definition with { Links = ["https://test.example/", "https://new-mirror.example/"] };

        Assert.Equal("https://test.example/", health.ActiveBaseUrl(updated));
    }
}
