using System.ComponentModel.DataAnnotations;

namespace Modularr.Core.Indexers;

public sealed record IndexerOptions
{
    // Cardigann YAML definitions (Prowlarr/Indexers v11 and v12), relative to the config directory.
    [Required]
    public string DefinitionsDirectory { get; init; } = "indexers";

    // Prowlarr refreshes its definitions every 24 hours.
    [Range(typeof(TimeSpan), "00:05:00", "30.00:00:00")]
    public TimeSpan DefinitionsUpdateInterval { get; init; } = TimeSpan.FromHours(24);

    // Ids of public indexers the user does not want searched; every other public video indexer is enabled.
    public IReadOnlyList<string> Disabled { get; init; } = [];

    // Per indexer id, a base URL to use instead of the definition's first link (a legacy one is migrated).
    public IReadOnlyDictionary<string, string> BaseUrls { get; init; } = new Dictionary<string, string>();

    // Per indexer id, the values of its definition's settings (credentials, cookie, options); a missing one takes its default.
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Settings { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, string>>();

    // Per HTTP request to an indexer (Prowlarr's default is 100 s; search UIs need an answer sooner).
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    // Whole search on one indexer (a definition may send several requests); slower indexers are reported as timed out.
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan SearchTimeout { get; init; } = TimeSpan.FromSeconds(60);

    // Indexer settings hold logins, cookies and keys: a non-admin sees none of them.
    public IndexerOptions Redacted() => this with { Settings = new Dictionary<string, IReadOnlyDictionary<string, string>>() };
}
