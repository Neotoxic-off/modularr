using System.ComponentModel.DataAnnotations;

namespace Modularr.Core.Indexers;

public sealed record IndexerOptions
{
    // Cardigann YAML definitions (Prowlarr/Indexers v11 and v12), relative to the config directory.
    [Required]
    public string DefinitionsDirectory { get; init; } = "indexers";

    // Per HTTP request to an indexer (Prowlarr's default is 100 s; search UIs need an answer sooner).
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
