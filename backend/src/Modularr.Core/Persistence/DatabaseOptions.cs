using System.ComponentModel.DataAnnotations;

namespace Modularr.Core.Persistence;

public sealed record DatabaseOptions
{
    // SQLite file, relative to the config directory.
    [Required]
    public string Path { get; init; } = "modularr.db";
}
