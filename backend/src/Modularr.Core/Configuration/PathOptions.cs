using System.ComponentModel.DataAnnotations;

namespace Modularr.Core.Configuration;

// Library and download roots; relative paths resolve against the config directory.
public sealed record PathOptions
{
    [Required]
    public string Series { get; init; } = "series";

    [Required]
    public string Movies { get; init; } = "movies";

    [Required]
    public string Anime { get; init; } = "anime";

    [Required]
    public string Downloads { get; init; } = "downloads";
}
