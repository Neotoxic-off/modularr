using System.ComponentModel.DataAnnotations;

namespace Modularr.Core.Metadata;

// Defaults are the endpoints Sonarr (SkyHook, services) and Radarr (RadarrAPI) ship with.
public sealed record MetadataOptions
{
    [Required]
    [Url]
    public string SkyHookUrl { get; init; } = "https://skyhook.sonarr.tv/v2/tvdb/";

    // Serves the daily-series list SkyHook does not carry.
    [Required]
    [Url]
    public string SonarrServicesUrl { get; init; } = "https://services.sonarr.tv/v1/";

    [Required]
    [Url]
    public string RadarrApiUrl { get; init; } = "https://api.radarr.video/v1/";
}
