using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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

    // Keyless discovery sources: TVmaze for shows, AniList for anime, Fribb's anime-lists to map AniList ids to TVDB.
    [Required]
    [Url]
    public string TvMazeUrl { get; init; } = "https://api.tvmaze.com/";

    [Required]
    [Url]
    public string AniListUrl { get; init; } = "https://graphql.anilist.co/";

    [Required]
    [Url]
    public string AnimeMappingUrl { get; init; } = "https://raw.githubusercontent.com/Fribb/anime-lists/master/anime-list-mini.json";

    public const string TmdbApiKeyVariable = "MODULARR_TMDB_API_KEY";

    // A TMDB v3 API key or v4 read access token: movie exploration with real filters. Without it, the keyless lists.
    public string? TmdbApiKey { get; init; }

    // Set from TmdbApiKeyVariable at startup, never from modularr.json; the settings key overrides it.
    [JsonIgnore]
    public string? EnvironmentTmdbApiKey { get; init; }

    [JsonIgnore]
    public string? EffectiveTmdbApiKey => NonBlank(TmdbApiKey) ?? NonBlank(EnvironmentTmdbApiKey);

    [JsonIgnore]
    public string TmdbKeySource => NonBlank(TmdbApiKey) is not null ? "Custom" : NonBlank(EnvironmentTmdbApiKey) is not null ? "Environment" : "None";

    // What a non-admin may see.
    public MetadataOptions Redacted() => this with { TmdbApiKey = null, EnvironmentTmdbApiKey = null };

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
