using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.MediaFiles;
using Modularr.Core.PostProcessing;

namespace Modularr.Core.Tests.Configuration;

public sealed class ModularrOptionsTests : IDisposable
{
    private readonly string _configDirectory = Directory.CreateTempSubdirectory("modularr-config-").FullName;

    public void Dispose() => Directory.Delete(_configDirectory, recursive: true);

    private ModularrOptions Load(string? json)
    {
        if (json is not null)
        {
            File.WriteAllText(Path.Combine(_configDirectory, ModularrOptions.FileName), json);
        }

        using ServiceProvider provider = new ServiceCollection().AddModularrCore(_configDirectory).BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ModularrOptions>>().Value;
    }

    [Fact]
    public void UsesRecordDefaultsWithoutAFile()
    {
        ModularrOptions options = Load(null);

        Assert.Equal(Path.Combine(_configDirectory, "series"), options.Paths.Series);
        Assert.Equal(Path.Combine(_configDirectory, "downloads"), options.Paths.Downloads);
        Assert.Equal(Path.Combine(_configDirectory, "torrents"), options.Torrents.StateDirectory);
        Assert.Equal(Path.Combine(_configDirectory, "indexers"), options.Indexers.DefinitionsDirectory);
        Assert.Equal(6881, options.Torrents.ListenPort);
        Assert.Equal("https://skyhook.sonarr.tv/v2/tvdb/", options.Metadata.SkyHookUrl);
        Assert.Equal("https://api.radarr.video/v1/", options.Metadata.RadarrApiUrl);
        Assert.Same(MediaFileExtensions.VideoExtensions, options.PostProcessing.VideoExtensions);
        Assert.Same(PostProcessingOptions.DefaultSubtitleExtensions, options.PostProcessing.SubtitleExtensions);
    }

    [Fact]
    public void OverridesOnlyTheConfiguredValues()
    {
        string movies = Path.Combine(_configDirectory, "elsewhere", "movies");
        ModularrOptions options = Load($$"""
            {
              "Paths": { "Movies": {{JsonSerializer.Serialize(movies)}}, "Anime": "media/anime" },
              "Torrents": { "ListenPort": 51413, "SeedRatioLimit": 2.5, "SeedTimeLimit": "2.00:00:00" }
            }
            """);

        Assert.Equal(movies, options.Paths.Movies);
        Assert.Equal(Path.GetFullPath("media/anime", _configDirectory), options.Paths.Anime);
        Assert.Equal(Path.Combine(_configDirectory, "series"), options.Paths.Series);
        Assert.Equal(51413, options.Torrents.ListenPort);
        Assert.Equal(2.5, options.Torrents.SeedRatioLimit);
        Assert.Equal(TimeSpan.FromDays(2), options.Torrents.SeedTimeLimit);
        Assert.Equal(150, options.Torrents.MaxConnections);
    }

    [Fact]
    public void ConfiguredExtensionListReplacesTheDefault()
    {
        ModularrOptions options = Load("""{ "PostProcessing": { "VideoExtensions": [".mkv", ".MP4"] } }""");

        Assert.Equal(2, options.PostProcessing.VideoExtensions.Count);
        Assert.Contains(".mp4", options.PostProcessing.VideoExtensions);
        Assert.DoesNotContain(".iso", options.PostProcessing.VideoExtensions);
        Assert.Same(PostProcessingOptions.DefaultIgnorableExtensions, options.PostProcessing.IgnorableExtensions);
    }

    [Theory]
    [InlineData("""{ "Torrents": { "ListenPort": 0 } }""")]
    [InlineData("""{ "Torrents": { "SeedRatioLimit": -1 } }""")]
    [InlineData("""{ "Paths": { "Series": "" } }""")]
    [InlineData("""{ "PostProcessing": { "SubtitleExtensions": ["srt"] } }""")]
    [InlineData("""{ "Metadata": { "SkyHookUrl": "not a url" } }""")]
    [InlineData("""{ "Metadata": { "RadarrApiUrl": "" } }""")]
    public void RejectsInvalidValues(string json)
    {
        Assert.Throws<OptionsValidationException>(() => Load(json));
    }

    [Fact]
    public void RejectsUnknownKeys()
    {
        Assert.Throws<InvalidOperationException>(() => Load("""{ "Torrent": { "ListenPort": 1 } }"""));
    }
}
