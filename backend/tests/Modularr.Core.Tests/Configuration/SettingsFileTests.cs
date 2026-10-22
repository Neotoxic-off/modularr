using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Languages;
using Modularr.Core.Library;

namespace Modularr.Core.Tests.Configuration;

public sealed class SettingsFileTests : IDisposable
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

        await provider.GetRequiredService<SettingsFile>().SavePathsAsync(paths, TestContext.Current.CancellationToken);

        Assert.Equal(paths, options.CurrentValue.Paths);
        Assert.True(Directory.Exists(paths.Downloads));
        JsonObject file = JsonNode.Parse(File.ReadAllText(ConfigFile))!.AsObject();
        Assert.Equal(["paths", "Torrents"], file.Select(p => p.Key));
        Assert.Equal(51413, (int)file["Torrents"]!["ListenPort"]!);
    }

    [Fact]
    public async Task MediaSettingsRoundTripByLanguageName()
    {
        using ServiceProvider provider = new ServiceCollection().AddModularrCore(_configDirectory).BuildServiceProvider();
        IOptionsMonitor<ModularrOptions> options = provider.GetRequiredService<IOptionsMonitor<ModularrOptions>>();
        MediaOptions media = new()
        {
            Anime = new MediaDefaults { ProfileId = 2, Language = Language.Original },
            DefaultLanguage = Language.French,
            Exceptions = [new LanguageRule { Category = MediaType.Movie, OriginalLanguage = Language.Japanese, Language = Language.English }],
        };

        await provider.GetRequiredService<SettingsFile>().SaveMediaAsync(media, TestContext.Current.CancellationToken);

        MediaOptions saved = options.CurrentValue.Media;
        Assert.Equal((2, Language.Original, Language.French), (saved.Anime.ProfileId, saved.Anime.Language, saved.DefaultLanguage));
        Assert.Equal(media.Exceptions, saved.Exceptions);
        Assert.Null(saved.Movies.Language);
        JsonNode file = JsonNode.Parse(File.ReadAllText(ConfigFile))!;
        Assert.Equal(("French", "Movie"), ((string?)file["Media"]!["DefaultLanguage"], (string?)file["Media"]!["Exceptions"]![0]!["Category"]));
    }

    [Fact]
    public async Task RejectsARelativePathNamingTheField()
    {
        using ServiceProvider provider = new ServiceCollection().AddModularrCore(_configDirectory).BuildServiceProvider();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetRequiredService<SettingsFile>().SavePathsAsync(Under("media") with { Anime = "anime" }, TestContext.Current.CancellationToken));

        Assert.StartsWith("anime:", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(ConfigFile));
    }
}
