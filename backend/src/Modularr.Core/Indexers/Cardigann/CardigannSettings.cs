using System.Net;
using System.Text.RegularExpressions;
using Modularr.Core.Indexers.Definitions;

namespace Modularr.Core.Indexers.Cardigann;

// The user's values for a definition's settings fields (Prowlarr's ExtraFieldData): which are secret, which a login needs.
public static partial class CardigannSettings
{
    private static readonly IReadOnlyDictionary<string, string> NoValues = new Dictionary<string, string>();

    // info* fields only show text; cardigannCaptcha is Prowlarr's captcha image, never stored.
    public static bool IsInfo(SettingsField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Type.StartsWith("info", StringComparison.Ordinal) || field.Type == "cardigannCaptcha";
    }

    public static bool IsSecret(SettingsField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Type == "password" || (field.Type == "text" && SecretName().IsMatch(field.Name));
    }

    // Credentials the definition reads (a cookie login reads "cookie" without a template); a 2FA code stays optional.
    public static IReadOnlyList<SettingsField> Required(CardigannDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return
        [
            .. definition.Settings.Where(field => field.Type is "text" or "password"
                && (IsSecret(field) || field.Name == "username")
                && !TwoFactorName().IsMatch(field.Name)
                && (definition.UsedSettings.Contains(field.Name) || (field.Name == "cookie" && definition.Login?.Method == "cookie"))),
        ];
    }

    public static bool NeedsLogin(CardigannDefinition definition) => Required(definition).Count > 0;

    public static bool IsConfigured(CardigannDefinition definition, IReadOnlyDictionary<string, string>? values) =>
        Required(definition).All(field => !string.IsNullOrWhiteSpace((values ?? NoValues).GetValueOrDefault(field.Name)));

    /// <summary>The stored values after an edit: unknown fields are refused, an empty secret keeps the stored one.</summary>
    /// <exception cref="ArgumentException">A field the definition does not have, or a value its type does not allow.</exception>
    public static IReadOnlyDictionary<string, string> Merge(CardigannDefinition definition, IReadOnlyDictionary<string, string>? current, IReadOnlyDictionary<string, string?> submitted)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(submitted);
        Dictionary<string, string> merged = new(current ?? NoValues, StringComparer.Ordinal);
        foreach ((string name, string? value) in submitted)
        {
            SettingsField field = definition.Settings.FirstOrDefault(f => f.Name == name && !IsInfo(f))
                ?? throw new ArgumentException($"{definition.Name} has no setting '{name}'.", nameof(submitted));
            string text = value?.Trim() ?? "";
            if (text.Length == 0)
            {
                if (!IsSecret(field))
                {
                    merged.Remove(name);
                }

                continue;
            }

            if ((field.Type == "checkbox" && !bool.TryParse(text, out _)) || (field.Type == "select" && field.Options?.ContainsKey(text) != true) || text.Length > 4096)
            {
                throw new ArgumentException($"'{field.Label ?? name}' does not accept this value.", nameof(submitted));
            }

            merged[name] = text;
        }

        return merged;
    }

    // Secret values (and their URL-encoded form, as in a passkey link) never reach an error, a log or the indexer health.
    public static string Scrub(string message, CardigannDefinition definition, IReadOnlyDictionary<string, string>? values)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(definition);
        foreach (SettingsField field in definition.Settings.Where(IsSecret))
        {
            if ((values ?? NoValues).GetValueOrDefault(field.Name) is { Length: >= 3 } secret)
            {
                message = message.Replace(secret, "***", StringComparison.Ordinal).Replace(WebUtility.UrlEncode(secret), "***", StringComparison.Ordinal);
            }
        }

        return message;
    }

    [GeneratedRegex(@"cookie|api_?key|pass_?key|rss_?key|token|secret|2fa|totp|^pid$|^uid$|hash", RegexOptions.IgnoreCase)]
    private static partial Regex SecretName();

    [GeneratedRegex(@"2fa|totp", RegexOptions.IgnoreCase)]
    private static partial Regex TwoFactorName();
}
