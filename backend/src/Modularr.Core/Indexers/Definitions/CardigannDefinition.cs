using YamlDotNet.Serialization;

namespace Modularr.Core.Indexers.Definitions;

// Cardigann YAML model (Prowlarr schema v11/v12); YAML keys match property names case-insensitively.
public sealed record CardigannDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Type { get; init; }
    public required string Language { get; init; }
    public string Encoding { get; init; } = "UTF-8";
    public double? RequestDelay { get; init; }

    // First link is the primary domain, the rest are failover mirrors.
    public required IReadOnlyList<string> Links { get; init; }

    // Dead or retired domains: a base URL configured on one of them is migrated to Links[0].
    public IReadOnlyList<string> LegacyLinks { get; init; } = [];

    public bool FollowRedirect { get; init; }
    public bool TestLinkTorrent { get; init; } = true;
    public IReadOnlyList<string>? Certificates { get; init; }
    public IReadOnlyList<SettingsField> Settings { get; init; } = DefaultSettings;
    public required CapabilitiesBlock Caps { get; init; }
    public LoginBlock? Login { get; init; }
    public RatioBlock? Ratio { get; init; }
    public required SearchBlock Search { get; init; }
    public DownloadBlock? Download { get; init; }

    // Setting names the templates read as .Config.<name>, filled by the loader from the YAML text.
    [YamlIgnore]
    public IReadOnlySet<string> UsedSettings { get; init; } = new HashSet<string>();

    public static IReadOnlyList<SettingsField> DefaultSettings { get; } =
    [
        new() { Name = "username", Label = "Username", Type = "text" },
        new() { Name = "password", Label = "Password", Type = "password" },
    ];

    public IndexerPrivacy Privacy => Type switch
    {
        "private" => IndexerPrivacy.Private,
        "public" => IndexerPrivacy.Public,
        _ => IndexerPrivacy.SemiPrivate,
    };

    public bool IsLegacyLink(string url) => LegacyLinks.Any(link => SameUrl(link, url));

    public string ResolveBaseUrl(string? configuredBaseUrl) =>
        string.IsNullOrWhiteSpace(configuredBaseUrl) || IsLegacyLink(configuredBaseUrl) ? Links[0] : configuredBaseUrl;

    // Active base URL first, then the definition's other links in order.
    public IReadOnlyList<string> CandidateBaseUrls(string? configuredBaseUrl)
    {
        string active = ResolveBaseUrl(configuredBaseUrl);
        return [active, .. Links.Where(link => !SameUrl(link, active))];
    }

    // Prowlarr compares exact strings; a missing trailing slash or upper-case host must not dodge the migration.
    private static bool SameUrl(string left, string right) =>
        string.Equals(left.TrimEnd('/'), right.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}

public enum IndexerPrivacy
{
    Public,
    SemiPrivate,
    Private,
}

public sealed record SettingsField
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Label { get; init; }
    public string? Default { get; init; }
    public IReadOnlyList<string>? Defaults { get; init; }
    public IReadOnlyDictionary<string, string>? Options { get; init; }
}

public sealed record CategoryMapping
{
    public required string Id { get; init; }
    public string? Cat { get; init; }
    public string? Desc { get; init; }
    public bool Default { get; init; }
}

public sealed record CapabilitiesBlock
{
    public IReadOnlyDictionary<string, string>? Categories { get; init; }
    public IReadOnlyList<CategoryMapping>? CategoryMappings { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Modes { get; init; }
    public bool AllowRawSearch { get; init; }
}

public sealed record CaptchaBlock
{
    public string? Type { get; init; }
    public string? Selector { get; init; }
    public string? Input { get; init; }
}

public sealed record LoginBlock
{
    public string? Path { get; init; }
    public string? SubmitPath { get; init; }
    public IReadOnlyList<string>? Cookies { get; init; }
    public string Method { get; init; } = "form";
    public string? Form { get; init; }
    public bool Selectors { get; init; }
    public IReadOnlyDictionary<string, string>? Inputs { get; init; }
    public IReadOnlyDictionary<string, SelectorBlock>? SelectorInputs { get; init; }
    public IReadOnlyDictionary<string, SelectorBlock>? GetSelectorInputs { get; init; }
    public IReadOnlyList<ErrorBlock>? Error { get; init; }
    public PageTestBlock? Test { get; init; }
    public CaptchaBlock? Captcha { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Headers { get; init; }
}

public sealed record ErrorBlock
{
    public string? Path { get; init; }
    public string? Selector { get; init; }
    public SelectorBlock? Message { get; init; }
}

public record SelectorBlock
{
    public string? Selector { get; init; }
    public bool Optional { get; init; }
    public string? Default { get; init; }
    public string? Text { get; init; }
    public string? Attribute { get; init; }
    public string? Remove { get; init; }
    public IReadOnlyList<FilterBlock>? Filters { get; init; }
    public IReadOnlyDictionary<string, string>? Case { get; init; }
}

public sealed record FilterBlock
{
    public required string Name { get; init; }

    // A scalar, a list or a map depending on the filter.
    public object? Args { get; init; }
}

public sealed record PageTestBlock
{
    public string? Path { get; init; }
    public string? Selector { get; init; }
}

public sealed record RatioBlock : SelectorBlock
{
    public string? Path { get; init; }
}

public sealed record SearchBlock
{
    public string? Path { get; init; }
    public IReadOnlyList<SearchPathBlock> Paths { get; init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Headers { get; init; }
    public IReadOnlyList<FilterBlock>? KeywordsFilters { get; init; }
    public bool AllowEmptyInputs { get; init; }
    public IReadOnlyDictionary<string, string>? Inputs { get; init; }
    public IReadOnlyList<ErrorBlock>? Error { get; init; }
    public IReadOnlyList<FilterBlock>? PreprocessingFilters { get; init; }
    public required RowsBlock Rows { get; init; }

    // Ordered and may repeat a name (e.g. a fallback selector for the same field).
    public required IReadOnlyList<SearchField> Fields { get; init; }
}

public sealed record SearchField(string Name, SelectorBlock Block);

public sealed record RowsBlock : SelectorBlock
{
    public int After { get; init; }
    public SelectorBlock? DateHeaders { get; init; }
    public SelectorBlock? Count { get; init; }
    public bool Multiple { get; init; }
    public bool MissingAttributeEqualsNoResults { get; init; }
}

public record RequestBlock
{
    public string? Path { get; init; }
    public string? Method { get; init; }
    public IReadOnlyDictionary<string, string>? Inputs { get; init; }
    public string QuerySeparator { get; init; } = "&";
}

public sealed record SearchPathBlock : RequestBlock
{
    public IReadOnlyList<string>? Categories { get; init; }
    public bool InheritInputs { get; init; } = true;
    public bool FollowRedirect { get; init; }
    public ResponseBlock? Response { get; init; }
}

public sealed record DownloadBlock
{
    public IReadOnlyList<SelectorField>? Selectors { get; init; }
    public string? Method { get; init; }
    public BeforeBlock? Before { get; init; }
    public InfoHashBlock? InfoHash { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Headers { get; init; }
}

public sealed record InfoHashBlock
{
    public SelectorField? Hash { get; init; }
    public SelectorField? Title { get; init; }
    public bool UseBeforeResponse { get; init; }
}

public sealed record SelectorField
{
    public string? Selector { get; init; }
    public string? Attribute { get; init; }
    public bool UseBeforeResponse { get; init; }
    public IReadOnlyList<FilterBlock>? Filters { get; init; }
}

public sealed record BeforeBlock : RequestBlock
{
    public SelectorField? PathSelector { get; init; }
}

public sealed record ResponseBlock
{
    public string? Type { get; init; }
    public string? NoResultsMessage { get; init; }
}
