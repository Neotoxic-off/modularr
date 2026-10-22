using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Modularr.Core.Decisions;
using Modularr.Core.Languages;
using Modularr.Core.Library;

namespace Modularr.Core.Tests.Library;

public sealed class MediaOptionsTests
{
    private static readonly MediaOptions Policy = new()
    {
        DefaultVersion = ReleaseVersion.VF,
        Series = new MediaDefaults { Version = ReleaseVersion.FindByName("German"), Subtitles = [Language.French] },
        Exceptions =
        [
            new VersionRule { Category = MediaType.Anime, Version = ReleaseVersion.VOSTFR },
            new VersionRule { OriginalLanguage = Language.English, Version = ReleaseVersion.VO },
            new VersionRule { OriginalLanguage = Language.Japanese, Version = ReleaseVersion.Multi },
        ],
    };

    [Theory]
    [InlineData(MediaType.Anime, "Japanese", null, "VOSTFR")] // first matching exception wins over the Japanese one
    [InlineData(MediaType.Movie, "English", null, "VO")]
    [InlineData(MediaType.Movie, "Japanese", null, "MULTi")]
    [InlineData(MediaType.Series, "Korean", null, "German")] // no exception: the category version
    [InlineData(MediaType.Movie, "Korean", null, "VF")] // no category version: the default
    [InlineData(MediaType.Movie, "English", "VFQ", "VFQ")] // the item's own override first
    public void WantedVersionFollowsThePolicyOrder(MediaType category, string original, string? itemVersion, string expected) =>
        Assert.Equal(
            ReleaseVersion.FindByName(expected),
            Policy.WantedVersion(category, Language.FindByName(original), itemVersion is null ? null : ReleaseVersion.FindByName(itemVersion)));

    [Fact]
    public void DefaultsWantTheOriginalVersionAndNoSubtitles()
    {
        Assert.Equal(ReleaseVersion.VO, new MediaOptions().WantedVersion(MediaType.Anime, Language.Japanese, null));
        Assert.Empty(new MediaOptions().WantedSubtitles(MediaType.Anime, null));
    }

    // Subtitles follow their own item and category settings whatever the audio version.
    [Fact]
    public void WantedSubtitlesIgnoreTheVersion()
    {
        MediaOptions policy = Policy with { Movies = new MediaDefaults { Version = ReleaseVersion.VF, Subtitles = [Language.English] } };

        Assert.Equal([Language.English], policy.WantedSubtitles(MediaType.Movie, null));
        Assert.Equal([Language.English], (policy with { DefaultVersion = ReleaseVersion.VOSTFR }).WantedSubtitles(MediaType.Movie, null));
        Assert.Equal([Language.German], policy.WantedSubtitles(MediaType.Movie, [Language.German]));
        Assert.Equal([Language.French], policy.WantedSubtitles(MediaType.Series, null));
    }

    [Fact]
    public void WantedSubtitlesAreTheItemsOrTheCategorys()
    {
        Assert.Equal([Language.French], Policy.WantedSubtitles(MediaType.Series, null));
        Assert.Empty(Policy.WantedSubtitles(MediaType.Series, []));
        Assert.Empty(Policy.WantedSubtitles(MediaType.Movie, null));
    }

    // The language policy's names still read, so old settings and columns keep their meaning.
    [Theory]
    [InlineData("French", "VF")]
    [InlineData("Original", "VO")]
    [InlineData("Any", "Any")]
    [InlineData("German", "German")]
    [InlineData("multi", "MULTi")]
    public void LanguageNamesMapToVersions(string name, string expected) =>
        Assert.Equal(expected, ReleaseVersion.FindByName(name).Name);

    [Fact]
    public void EveryVersionHasOneName()
    {
        Assert.Equal(ReleaseVersion.All.Count, ReleaseVersion.All.Select(v => v.Name.ToUpperInvariant()).Distinct().Count());
        Assert.DoesNotContain(ReleaseVersion.All, v => v.Name is "French" or "Original" or "Unknown");
        Assert.Throws<ArgumentException>(() => ReleaseVersion.FindByName("Unknown"));
    }

    [Fact]
    public void AnExceptionNeedsACondition()
    {
        MediaOptions media = new() { Exceptions = [new VersionRule { Version = ReleaseVersion.VF }] };

        Assert.Single(media.Validate(new ValidationContext(media)));
        Assert.Empty(Policy.Validate(new ValidationContext(Policy)));
    }

    [Theory]
    [InlineData("""{ "movies": null }""")]
    [InlineData("""{ "exceptions": null }""")]
    [InlineData("""{ "exceptions": [{ "originalLanguage": "Japanese", "version": null }] }""")]
    [InlineData("""{ "anime": { "profileId": 1, "subtitles": ["Original"] } }""")]
    [InlineData("""{ "series": { "profileId": 1, "subtitles": ["French", "French"] } }""")]
    [InlineData("""{ "movies": { "profileId": 1, "subtitles": null } }""")]
    public void ANullSectionOrABadSubtitleLanguageIsABadRequest(string json)
    {
        MediaOptions media = JsonSerializer.Deserialize<MediaOptions>(json, JsonSerializerOptions.Web)!;

        Assert.Throws<ArgumentException>(media.EnsureValid);
    }

    [Fact]
    public void ItemSubtitlesMustBeRealLanguages()
    {
        Assert.Throws<ArgumentException>(() => MediaOptions.EnsureSubtitleLanguages([Language.Unknown]));
        Assert.Equal([Language.French], MediaOptions.EnsureSubtitleLanguages([Language.French]));
        Assert.Null(MediaOptions.EnsureSubtitleLanguages(null));
    }
}
