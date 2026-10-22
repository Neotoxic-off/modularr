using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Modularr.Core.Indexers.Definitions;

namespace Modularr.Core.Indexers.Cardigann;

// A definition bound to its active base URL: what Prowlarr's CardigannBase holds per indexer.
internal sealed class CardigannSite
{
    static CardigannSite() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public CardigannSite(CardigannDefinition definition, string? configuredBaseUrl)
    {
        Definition = definition;
        string baseUrl = definition.ResolveBaseUrl(configuredBaseUrl);

        // Paths are resolved against the base URL, so it must end with a slash to keep its own path.
        SiteLink = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        Encoding = Encoding.GetEncoding(definition.Encoding);
        Categories = new CardigannCategoryMap(definition.Caps);
    }

    public CardigannDefinition Definition { get; }
    public Uri SiteLink { get; }
    public Encoding Encoding { get; }
    public CardigannCategoryMap Categories { get; }

    public Uri ResolvePath(string? path, Uri? relativeTo = null) => new(relativeTo ?? SiteLink, path);

    /// <exception cref="NotSupportedException">A setting type upstream does not know either.</exception>
    public Dictionary<string, object?> BaseVariables()
    {
        Dictionary<string, object?> variables = new(StringComparer.Ordinal)
        {
            [".Config.sitelink"] = SiteLink.AbsoluteUri,
            [".True"] = "True",
            [".False"] = null,
            [".Today.Year"] = DateTime.Today.Year.ToString(CultureInfo.InvariantCulture),
        };

        // shortcut: settings always take their definition defaults (no per-indexer settings yet), add a settings store when the UI edits them.
        foreach (SettingsField setting in Definition.Settings)
        {
            string name = ".Config." + setting.Name;
            switch (setting.Type)
            {
                case "text" or "password":
                    variables[name] = setting.Default;
                    break;
                case "checkbox":
                    variables[name] = bool.TryParse(setting.Default, out bool isChecked) && isChecked ? ".True" : null;
                    break;
                case "select":
                    // The default option, or the first one by key (upstream fails without a valid default).
                    IReadOnlyDictionary<string, string> options = setting.Options ?? new Dictionary<string, string>();
                    variables[name] = setting.Default is not null && options.ContainsKey(setting.Default)
                        ? setting.Default
                        : options.Keys.Order(StringComparer.Ordinal).FirstOrDefault();
                    break;
                case "info" or "info_cookie" or "info_flaresolverr" or "info_useragent" or "info_category_8000" or "cardigannCaptcha":
                    break;
                default:
                    throw new NotSupportedException($"Setting type {setting.Type} is not supported.");
            }
        }

        return variables;
    }

    public static void AddUriVariables(Dictionary<string, object?> variables, Uri uri, string prefix)
    {
        variables[prefix + ".AbsoluteUri"] = uri.AbsoluteUri;
        variables[prefix + ".AbsolutePath"] = uri.AbsolutePath;
        variables[prefix + ".Scheme"] = uri.Scheme;
        variables[prefix + ".Host"] = uri.Host;
        variables[prefix + ".Port"] = uri.Port.ToString(CultureInfo.InvariantCulture);
        variables[prefix + ".PathAndQuery"] = uri.PathAndQuery;
        variables[prefix + ".Query"] = uri.Query;

        // A repeated parameter keeps its first value.
        foreach ((string key, StringValues values) in QueryHelpers.ParseQuery(uri.Query))
        {
            variables[prefix + ".Query." + key] = values.FirstOrDefault();
        }
    }

    // Upstream sends only the first value of a repeated header.
    public static Dictionary<string, string> Headers(IReadOnlyDictionary<string, IReadOnlyList<string>>? headers, IReadOnlyDictionary<string, object?> variables) =>
        (headers ?? new Dictionary<string, IReadOnlyList<string>>())
            .Where(header => header.Value.Count > 0)
            .ToDictionary(header => header.Key, header => CardigannTemplate.Apply(header.Value[0], variables) ?? "", StringComparer.OrdinalIgnoreCase);
}
