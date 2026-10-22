using System.ComponentModel.DataAnnotations;
using Modularr.Core.Decisions;
using Modularr.Core.Languages;
using Modularr.Core.Persistence;

namespace Modularr.Core.Library;

// What a newly added item gets, per category: its quality profile, the version wanted (null: DefaultVersion) and the subtitle
// languages wanted, most wanted first.
public sealed record MediaDefaults
{
    public int ProfileId { get; init; } = 1;
    public ReleaseVersion? Version { get; init; }
    public IReadOnlyList<Language> Subtitles { get; init; } = [];
}

// "anime -> VOSTFR", "originalLanguage Japanese -> VF": a null condition matches anything, at least one is set.
public sealed record VersionRule
{
    public MediaType? Category { get; init; }
    public Language? OriginalLanguage { get; init; }
    public ReleaseVersion Version { get; init; } = ReleaseVersion.Any;

    public bool Matches(MediaType category, Language originalLanguage) =>
        (Category is null || Category == category) && (OriginalLanguage is null || OriginalLanguage == originalLanguage);
}

// The modularr.json "Media" section, edited through PUT /api/settings/media. It is the single version policy: quality
// profiles carry no language. Wanted version = item override ?? first matching exception ?? category version ?? DefaultVersion.
// Wanted subtitles = item override ?? category subtitles.
public sealed record MediaOptions : IValidatableObject
{
    public MediaDefaults Movies { get; init; } = new();
    public MediaDefaults Series { get; init; } = new();
    public MediaDefaults Anime { get; init; } = new();

    // Any disables the version check.
    public ReleaseVersion DefaultVersion { get; init; } = ReleaseVersion.Any;
    public IReadOnlyList<VersionRule> Exceptions { get; init; } = [];

    public MediaDefaults For(MediaType category) => category switch
    {
        MediaType.Movie => Movies,
        MediaType.Anime => Anime,
        _ => Series,
    };

    public ReleaseVersion WantedVersion(MediaType category, Language originalLanguage, ReleaseVersion? itemVersion) =>
        itemVersion
        ?? Exceptions.FirstOrDefault(rule => rule.Matches(category, originalLanguage))?.Version
        ?? For(category).Version
        ?? DefaultVersion;

    public ReleaseVersion WantedVersion(Series series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return WantedVersion(series.MediaType, series.OriginalLanguage, series.Version);
    }

    public ReleaseVersion WantedVersion(Movie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        return WantedVersion(MediaType.Movie, movie.OriginalLanguage, movie.Version);
    }

    public IReadOnlyList<Language> WantedSubtitles(MediaType category, IReadOnlyList<Language>? itemSubtitles) => itemSubtitles ?? For(category).Subtitles;

    public IReadOnlyList<Language> WantedSubtitles(Series series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return WantedSubtitles(series.MediaType, series.Subtitles);
    }

    public IReadOnlyList<Language> WantedSubtitles(Movie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        return WantedSubtitles(MediaType.Movie, movie.Subtitles);
    }

    // Real languages only (not Unknown, Any or Original), each once.
    public static IReadOnlyList<Language>? EnsureSubtitleLanguages(IReadOnlyList<Language>? languages) =>
        languages is null || AreSubtitleLanguages(languages)
            ? languages
            : throw new ArgumentException("Subtitle languages must be real languages, each listed once.", nameof(languages));

    private static bool AreSubtitleLanguages(IReadOnlyList<Language> languages) =>
        languages.All(l => l is not null && l.Id > 0) && languages.Distinct().Count() == languages.Count;

    // Throws the first validation error as an ArgumentException (a 400 from the API).
    public void EnsureValid()
    {
        if (Validate(new ValidationContext(this)).FirstOrDefault() is ValidationResult error)
        {
            throw new ArgumentException(error.ErrorMessage);
        }
    }

    // Checked at startup (options validation) and before PUT /api/settings/media writes the file.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // A JSON body can null any of these despite the non-nullable types.
        if (Movies is null || Series is null || Anime is null || DefaultVersion is null || Exceptions is null || Exceptions.Any(rule => rule?.Version is null)
            || Movies.Subtitles is null || Series.Subtitles is null || Anime.Subtitles is null)
        {
            yield return new ValidationResult("movies, series, anime, their subtitles, defaultVersion, exceptions and each exception's version are required.");
            yield break;
        }

        foreach ((string name, MediaDefaults defaults) in new[] { ("movies", Movies), ("series", Series), ("anime", Anime) })
        {
            if (defaults.ProfileId < 1)
            {
                yield return new ValidationResult($"{name}: profileId must be a quality profile id.");
            }

            if (!AreSubtitleLanguages(defaults.Subtitles))
            {
                yield return new ValidationResult($"{name}: subtitles must be real languages, each listed once.");
            }
        }

        if (Exceptions.Any(rule => rule.Category is null && rule.OriginalLanguage is null))
        {
            yield return new ValidationResult("An exception needs a category, an original language, or both.");
        }
    }
}
