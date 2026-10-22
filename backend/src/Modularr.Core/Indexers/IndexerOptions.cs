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
}
