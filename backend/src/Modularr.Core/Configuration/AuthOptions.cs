using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Modularr.Core.Configuration;

// The key request apps (Overseerr, Jellyseerr, Ombi) send to the /api/v3 shim. None configured: key sign-in is off.
// It is also the key linkarr provisions the accounts with: set from ApiKeyVariable, it overrides modularr.json.
public sealed partial record AuthOptions
{
    public const string ApiKeyVariable = "MODULARR_API_KEY";

    public string? ApiKey { get; init; }

    // Set at startup from ApiKeyVariable, never from modularr.json: the key then cannot be regenerated here.
    [JsonIgnore]
    public bool ApiKeyFromEnvironment { get; init; }

    public AuthOptions Redacted() => this with { ApiKey = null };

    // An empty variable counts as unset, as compose files often leave it.
    public AuthOptions WithEnvironmentKey(string? key) => string.IsNullOrEmpty(key)
        ? this with { ApiKeyFromEnvironment = false }
        : this with { ApiKey = key, ApiKeyFromEnvironment = true };

    [JsonIgnore]
    public bool HasValidEnvironmentKey => !ApiKeyFromEnvironment || (ApiKey is not null && EnvironmentKeyPattern().IsMatch(ApiKey));

    [GeneratedRegex("^[A-Za-z0-9_-]{32,}$")]
    private static partial Regex EnvironmentKeyPattern();

    public static string GenerateApiKey() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    // Both sides hashed first: a fixed-length, constant-time comparison whatever the presented length.
    public static bool ApiKeyMatches(string? configured, string? presented) =>
        !string.IsNullOrEmpty(configured) && !string.IsNullOrEmpty(presented)
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(configured)), SHA256.HashData(Encoding.UTF8.GetBytes(presented)));
}
