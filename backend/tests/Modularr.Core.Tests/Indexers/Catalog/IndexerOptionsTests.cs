using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Catalog;

namespace Modularr.Core.Tests.Indexers.Catalog;

public sealed class IndexerOptionsTests : IDisposable
{
    private readonly string _configDirectory = Directory.CreateTempSubdirectory("modularr-config-").FullName;

    public void Dispose() => Directory.Delete(_configDirectory, recursive: true);

    private IndexerOptions Load(string? json)
    {
        if (json is not null)
        {
            File.WriteAllText(Path.Combine(_configDirectory, ModularrOptions.FileName), json);
        }

        using ServiceProvider provider = new ServiceCollection().AddModularrCore(_configDirectory).BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IndexerCatalogHost>());
        Assert.NotNull(provider.GetRequiredService<IndexerDomainHealth>());
        return provider.GetRequiredService<IOptions<ModularrOptions>>().Value.Indexers;
    }

    [Fact]
    public void DefaultsEnableEveryPublicIndexerAndSyncDaily()
    {
        IndexerOptions options = Load(null);

        Assert.Empty(options.Disabled);
        Assert.Empty(options.BaseUrls);
        Assert.Equal(TimeSpan.FromHours(24), options.DefinitionsUpdateInterval);
    }

    [Fact]
    public void BindsTheDenyListBaseUrlsAndInterval()
    {
        IndexerOptions options = Load("""
            { "Indexers": { "Disabled": ["1337x"], "BaseUrls": { "1337x": "https://x1337x.eu/" }, "DefinitionsUpdateInterval": "06:00:00" } }
            """);

        Assert.Equal(["1337x"], options.Disabled);
        Assert.Equal("https://x1337x.eu/", options.BaseUrls["1337x"]);
        Assert.Equal(TimeSpan.FromHours(6), options.DefinitionsUpdateInterval);
    }

    [Theory]
    [InlineData("00:01:00")]
    [InlineData("60.00:00:00")]
    public void RejectsAnIntervalOutOfRange(string interval)
    {
        Assert.Throws<OptionsValidationException>(() => Load($$"""{ "Indexers": { "DefinitionsUpdateInterval": "{{interval}}" } }"""));
    }
}
