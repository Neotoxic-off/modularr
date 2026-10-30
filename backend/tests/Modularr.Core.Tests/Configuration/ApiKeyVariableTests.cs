using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modularr.Api;
using Modularr.Core.Configuration;

namespace Modularr.Core.Tests.Configuration;

// MODULARR_API_KEY is process-wide: these run alone, so no other test builds its options while it is set.
[CollectionDefinition(nameof(ProcessEnvironment), DisableParallelization = true)]
public sealed class ProcessEnvironment;

[Collection(nameof(ProcessEnvironment))]
public sealed class ApiKeyVariableTests : IDisposable
{
    private const string EnvironmentKey = "linkarr-provisioned_key-0123456789abcdef";

    private readonly string _configDirectory = Directory.CreateTempSubdirectory("modularr-apikey-").FullName;

    public ApiKeyVariableTests() =>
        File.WriteAllText(Path.Combine(_configDirectory, ModularrOptions.FileName), """{ "Auth": { "ApiKey": "file-key" } }""");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AuthOptions.ApiKeyVariable, null);
        Directory.Delete(_configDirectory, recursive: true);
    }

    private ServiceProvider Build(string? variable)
    {
        Environment.SetEnvironmentVariable(AuthOptions.ApiKeyVariable, variable);
        return new ServiceCollection().AddModularrCore(_configDirectory).BuildServiceProvider();
    }

    [Fact]
    public void TheVariableOverridesTheFileKey()
    {
        using ServiceProvider provider = Build(EnvironmentKey);
        provider.GetRequiredService<IStartupValidator>().Validate();

        AuthOptions auth = provider.GetRequiredService<IOptions<ModularrOptions>>().Value.Auth;
        Assert.Equal((EnvironmentKey, true), (auth.ApiKey, auth.ApiKeyFromEnvironment));
    }

    // GET /api/settings/apikey: the deployment's key never comes back; a key generated here does.
    [Fact]
    public void AnEnvironmentKeyIsNeverReadBack()
    {
        using ServiceProvider fromEnvironment = Build(EnvironmentKey);
        Assert.Equal(new ApiKeyAnswer(null, true), ApiKeyAnswer.Of(fromEnvironment.GetRequiredService<IOptions<ModularrOptions>>().Value.Auth));

        using ServiceProvider fromFile = Build(null);
        Assert.Equal(new ApiKeyAnswer("file-key", false), ApiKeyAnswer.Of(fromFile.GetRequiredService<IOptions<ModularrOptions>>().Value.Auth));
    }

    // linkarr's hook token is bound from modularr.json but no answer serializes it, and it travels in a header, never the URL.
    [Fact]
    public void TheManagedHookTokenIsNeverReadable()
    {
        ManagedOptions managed = new() { Name = "linkarr", Url = "http://linkarr:8070", HookToken = "hook-secret" };

        Assert.DoesNotContain("hook-secret", System.Text.Json.JsonSerializer.Serialize(managed), StringComparison.Ordinal);
        Assert.DoesNotContain("hook-secret", managed.Hook()!.Url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WithoutTheVariableTheFileKeyStays(string? variable)
    {
        using ServiceProvider provider = Build(variable);

        AuthOptions auth = provider.GetRequiredService<IOptions<ModularrOptions>>().Value.Auth;
        Assert.Equal(("file-key", false), (auth.ApiKey, auth.ApiKeyFromEnvironment));
    }

    [Theory]
    [InlineData("too-short")]
    [InlineData("has space 0123456789abcdef0123456789abcdef")]
    [InlineData("quote\"0123456789abcdef0123456789abcdef")]
    public void AnInvalidVariableFailsStartup(string variable)
    {
        using ServiceProvider provider = Build(variable);

        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains(AuthOptions.ApiKeyVariable, error.Message, StringComparison.Ordinal);
    }
}
