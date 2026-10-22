namespace Modularr.Core.Subtitles;

// The modularr.json "Subtitles" section, edited through PUT /api/settings/subtitles. The wanted languages are per category and
// per item (MediaOptions.WantedSubtitles); this holds what applies to all of them and the provider accounts.
public sealed record SubtitleOptions
{
    // A wanted subtitle language is skipped (download and release ranking) when the audio is already in it.
    public bool OnlyWhenAudioDiffers { get; init; } = true;

    // Among equally good subtitles, take the hearing-impaired ones (otherwise the plain ones).
    public bool PreferHearingImpaired { get; init; }

    public OpenSubtitlesOptions OpenSubtitles { get; init; } = new();

    // What a non-admin may see: the provider secrets removed.
    public SubtitleOptions Redacted() => this with { OpenSubtitles = OpenSubtitles with { ApiKey = null, Password = null } };
}

// OpenSubtitles.com REST v1: the provider is used once an API key is set; an account raises the daily download quota.
public sealed record OpenSubtitlesOptions
{
    public string? ApiKey { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }

    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);
}
