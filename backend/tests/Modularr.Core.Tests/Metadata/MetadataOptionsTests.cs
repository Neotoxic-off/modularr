using Modularr.Core.Metadata;

namespace Modularr.Core.Tests.Metadata;

public sealed class MetadataOptionsTests
{
    [Theory]
    [InlineData(null, null, null, "None")]
    [InlineData("  ", null, null, "None")]
    [InlineData(null, "env-key", "env-key", "Environment")]
    [InlineData("custom", "env-key", "custom", "Custom")]
    [InlineData(" custom ", null, "custom", "Custom")]
    [InlineData("", " env-key ", "env-key", "Environment")]
    public void TheSettingsKeyOverridesTheEnvironmentKey(string? custom, string? environment, string? effective, string source)
    {
        MetadataOptions options = new() { TmdbApiKey = custom, EnvironmentTmdbApiKey = environment };

        Assert.Equal(effective, options.EffectiveTmdbApiKey);
        Assert.Equal(source, options.TmdbKeySource);
    }

    [Fact]
    public void RedactedHidesBothKeys()
    {
        MetadataOptions redacted = new MetadataOptions { TmdbApiKey = "custom", EnvironmentTmdbApiKey = "env-key" }.Redacted();

        Assert.Null(redacted.EffectiveTmdbApiKey);
        Assert.Equal("None", redacted.TmdbKeySource);
    }
}
