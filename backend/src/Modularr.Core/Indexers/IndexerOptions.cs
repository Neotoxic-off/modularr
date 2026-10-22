using System.ComponentModel.DataAnnotations;

namespace Modularr.Core.Indexers;

public sealed record IndexerOptions
{
    // Cardigann YAML definitions (Prowlarr/Indexers v11 and v12), relative to the config directory.
    [Required]
    public string DefinitionsDirectory { get; init; } = "indexers";
}
