using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Languages;
using Modularr.Core.Library;
using Modularr.Core.Subtitles;

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
    public async Task MediaSettingsRoundTripByName()
    {
        using ServiceProvider provider = new ServiceCollection().AddModularrCore(_configDirectory).BuildServiceProvider();
        IOptionsMonitor<ModularrOptions> options = provider.GetRequiredService<IOptionsMonitor<ModularrOptions>>();
        MediaOptions media = new()
        {
            Anime = new MediaDefaults { ProfileId = 2, Version = ReleaseVersion.VOSTFR, Subtitles = [Language.French, Language.English] },
            DefaultVersion = ReleaseVersion.VF,
            Exceptions = [new VersionRule { Category = MediaType.Movie, OriginalLanguage = Language.Japanese, Version = ReleaseVersion.FindByName("German") }],
        };

        await provider.GetRequiredService<SettingsFile>().SaveMediaAsync(media, TestContext.Current.CancellationToken);

        MediaOptions saved = options.CurrentValue.Media;
        Assert.Equal((2, ReleaseVersion.VOSTFR, ReleaseVersion.VF), (saved.Anime.ProfileId, saved.Anime.Version, saved.DefaultVersion));
        Assert.Equal([Language.French, Language.English], saved.Anime.Subtitles);
        Assert.Equal(media.Exceptions, saved.Exceptions);
        Assert.Null(saved.Movies.Version);
        JsonNode file = JsonNode.Parse(File.ReadAllText(ConfigFile))!;
        Assert.Equal(("VF", "Movie"), ((string?)file["Media"]!["DefaultVersion"], (string?)file["Media"]!["Exceptions"]![0]!["Category"]));
    }

    [Fact]
    public void AnUnknownLanguageIsDroppedInsteadOfStoppingTheApp()
    {
        File.WriteAllText(ConfigFile, """{ "Media": { "Movies": { "Language": "Klingon" } } }""");

        Assert.True(SettingsFile.MigrateLanguagePolicy(_configDirectory));
        Assert.Null(JsonNode.Parse(File.ReadAllText(ConfigFile))!["Media"]!["Movies"]!["Version"]);
    }

    [Fact]
    public void TheLanguagePolicyIsMigratedToVersionsBeforeBinding()
    {
        File.WriteAllText(ConfigFile, """
            {
              "Torrents": { "ListenPort": 51413 },
              "Media": {
                "DefaultLanguage": "French",
                "Movies": { "ProfileId": 4, "Language": "Original" },
                "anime": { "language": "German" },
                "Series": { "Language": 2 },
                "Exceptions": [ { "Category": "Anime", "Language": "Any" }, { "OriginalLanguage": "Japanese", "Language": "French" } ]
              }
            }
            """);

        using ServiceProvider provider = new ServiceCollection().AddModularrCore(_configDirectory).BuildServiceProvider();

        MediaOptions media = provider.GetRequiredService<IOptions<ModularrOptions>>().Value.Media;
        Assert.Equal((ReleaseVersion.VF, ReleaseVersion.VO, 4), (media.DefaultVersion, media.Movies.Version, media.Movies.ProfileId));
        Assert.Equal(ReleaseVersion.FindByName("German"), media.Anime.Version);
        Assert.Equal(ReleaseVersion.VF, media.Series.Version); // a numeric language id
        Assert.Equal([ReleaseVersion.Any, ReleaseVersion.VF], media.Exceptions.Select(rule => rule.Version));
        Assert.False(SettingsFile.MigrateLanguagePolicy(_configDirectory)); // once only
        Assert.Equal(51413, (int)JsonNode.Parse(File.ReadAllText(ConfigFile))!["Torrents"]!["ListenPort"]!);
    }

    [Fact]
    public async Task SubtitleSettingsRoundTrip()
    {
        using ServiceProvider provider = new ServiceCollection().AddModularrCore(_configDirectory).BuildServiceProvider();
        SubtitleOptions subtitles = new() { OnlyWhenAudioDiffers = false, OpenSubtitles = new OpenSubtitlesOptions { ApiKey = "key", Username = "me", Password = "secret" } };

        await provider.GetRequiredService<SettingsFile>().SaveSubtitlesAsync(subtitles, TestContext.Current.CancellationToken);

        SubtitleOptions saved = provider.GetRequiredService<IOptionsMonitor<ModularrOptions>>().CurrentValue.Subtitles;
        Assert.Equal(subtitles, saved);
        Assert.DoesNotContain("Enabled", File.ReadAllText(ConfigFile), StringComparison.Ordinal); // derived: the binder would refuse it at the next start
        Assert.Equal((null, "me", null), (saved.Redacted().OpenSubtitles.ApiKey, saved.Redacted().OpenSubtitles.Username, saved.Redacted().OpenSubtitles.Password));
        Assert.Null(provider.GetRequiredService<IOptionsMonitor<ModularrOptions>>().CurrentValue.Redacted().Subtitles.OpenSubtitles.ApiKey);
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
