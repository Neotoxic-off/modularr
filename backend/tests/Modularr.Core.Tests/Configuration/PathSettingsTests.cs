using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;

namespace Modularr.Core.Tests.Configuration;

public sealed class PathSettingsTests : IDisposable
{
    private readonly string _configDirectory = Directory.CreateTempSubdirectory("modularr-config-").FullName;

    public void Dispose() => Directory.Delete(_configDirectory, recursive: true);

    private string ConfigFile => Path.Combine(_configDirectory, ModularrOptions.FileName);

    private PathOptions Under(string name) => new()
    {
        Series = Path.Combine(_configDirectory, name, "tv"),
        Movies = Path.Combine(_configDirectory, name, "movies"),
        Anime = Path.Combine(_configDirectory, name, "anime"),
        Downloads = Path.Combine(_configDirectory, name, "downloads"),
    };

    [Fact]
    public async Task SavesOnlyThePathsSectionAndAppliesItLive()
    {
        File.WriteAllText(ConfigFile, """{ "paths": { "Series": "old" }, "Torrents": { "ListenPort": 51413 } }""");
        using ServiceProvider provider = new ServiceCollection().AddModularrCore(_configDirectory).BuildServiceProvider();
        IOptionsMonitor<ModularrOptions> options = provider.GetRequiredService<IOptionsMonitor<ModularrOptions>>();
        Assert.Equal(Path.Combine(_configDirectory, "old"), options.CurrentValue.Paths.Series);
        PathOptions paths = Under("media");

        await provider.GetRequiredService<PathSettings>().SaveAsync(paths, TestContext.Current.CancellationToken);

        Assert.Equal(paths, options.CurrentValue.Paths);
        Assert.True(Directory.Exists(paths.Downloads));
        JsonObject file = JsonNode.Parse(File.ReadAllText(ConfigFile))!.AsObject();
        Assert.Equal(["paths", "Torrents"], file.Select(p => p.Key));
        Assert.Equal(51413, (int)file["Torrents"]!["ListenPort"]!);
    }

    [Fact]
    public async Task RejectsARelativePathNamingTheField()
    {
        using ServiceProvider provider = new ServiceCollection().AddModularrCore(_configDirectory).BuildServiceProvider();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetRequiredService<PathSettings>().SaveAsync(Under("media") with { Anime = "anime" }, TestContext.Current.CancellationToken));

        Assert.StartsWith("anime:", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(ConfigFile));
    }
}
