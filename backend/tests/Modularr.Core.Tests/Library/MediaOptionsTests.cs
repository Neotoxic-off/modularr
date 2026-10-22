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
        DefaultLanguage = Language.French,
        Series = new MediaDefaults { Language = Language.German },
        Exceptions =
        [
            new LanguageRule { Category = MediaType.Anime, Language = Language.Original },
            new LanguageRule { OriginalLanguage = Language.English, Language = Language.Original },
            new LanguageRule { OriginalLanguage = Language.Japanese, Language = Language.French },
        ],
    };

    [Theory]
    [InlineData(MediaType.Anime, "Japanese", null, "Original")] // first matching exception wins over the Japanese one
    [InlineData(MediaType.Movie, "English", null, "Original")]
    [InlineData(MediaType.Movie, "Japanese", null, "French")]
    [InlineData(MediaType.Series, "Korean", null, "German")] // no exception: the category language
    [InlineData(MediaType.Movie, "Korean", null, "French")] // no category language: the default
    [InlineData(MediaType.Movie, "English", "Spanish", "Spanish")] // the item's own override first
    public void WantedLanguageFollowsThePolicyOrder(MediaType category, string original, string? itemLanguage, string expected) =>
        Assert.Equal(
            Language.FindByName(expected),
            Policy.WantedLanguage(category, Language.FindByName(original), itemLanguage is null ? null : Language.FindByName(itemLanguage)));

    [Fact]
    public void DefaultsAcceptEveryLanguage() =>
        Assert.Equal(Language.Any, new MediaOptions().WantedLanguage(MediaType.Anime, Language.Japanese, null));

    [Fact]
    public void AnExceptionNeedsACondition()
    {
        MediaOptions media = new() { Exceptions = [new LanguageRule { Language = Language.French }] };

        Assert.Single(media.Validate(new ValidationContext(media)));
        Assert.Empty(Policy.Validate(new ValidationContext(Policy)));
    }

    [Theory]
    [InlineData("""{ "movies": null }""")]
    [InlineData("""{ "exceptions": null }""")]
    [InlineData("""{ "exceptions": [{ "originalLanguage": "Japanese", "language": null }] }""")]
    [InlineData("""{ "defaultLanguage": "Unknown" }""")]
    [InlineData("""{ "anime": { "profileId": 1, "language": "Unknown" } }""")]
    public void ANullSectionOrAnUnknownLanguageIsABadRequest(string json)
    {
        MediaOptions media = JsonSerializer.Deserialize<MediaOptions>(json, JsonSerializerOptions.Web)!;

        Assert.Throws<ArgumentException>(media.EnsureValid);
    }

    [Fact]
    public void UnknownIsNotAnItemLanguage()
    {
        Assert.Throws<ArgumentException>(() => MediaOptions.EnsureWantable(Language.Unknown));
        Assert.Equal(Language.French, MediaOptions.EnsureWantable(Language.French));
        Assert.Null(MediaOptions.EnsureWantable(null));
    }
}
