using System.ComponentModel.DataAnnotations;
using Modularr.Core.Decisions;
using Modularr.Core.Languages;
using Modularr.Core.Persistence;

namespace Modularr.Core.Library;

// What a newly added item gets, per category: its quality profile and the audio language wanted (null: DefaultLanguage).
public sealed record MediaDefaults
{
    public int ProfileId { get; init; } = 1;
    public Language? Language { get; init; }
}

// "anime -> Original", "originalLanguage Japanese -> French": a null condition matches anything, at least one is set.
public sealed record LanguageRule
{
    public MediaType? Category { get; init; }
    public Language? OriginalLanguage { get; init; }
    public Language Language { get; init; } = Language.Any;

    public bool Matches(MediaType category, Language originalLanguage) =>
        (Category is null || Category == category) && (OriginalLanguage is null || OriginalLanguage == originalLanguage);
}

// The modularr.json "Media" section, edited through PUT /api/settings/media. It is the single language policy: quality
// profiles carry no language. Wanted language = item override ?? first matching exception ?? category language ?? DefaultLanguage.
public sealed record MediaOptions : IValidatableObject
{
    public MediaDefaults Movies { get; init; } = new();
    public MediaDefaults Series { get; init; } = new();
    public MediaDefaults Anime { get; init; } = new();

    // Any disables the language check; Original is each item's original language.
    public Language DefaultLanguage { get; init; } = Language.Any;
    public IReadOnlyList<LanguageRule> Exceptions { get; init; } = [];

    public MediaDefaults For(MediaType category) => category switch
    {
        MediaType.Movie => Movies,
        MediaType.Anime => Anime,
        _ => Series,
    };

    public Language WantedLanguage(MediaType category, Language originalLanguage, Language? itemLanguage) =>
        itemLanguage
        ?? Exceptions.FirstOrDefault(rule => rule.Matches(category, originalLanguage))?.Language
        ?? For(category).Language
        ?? DefaultLanguage;

    public Language WantedLanguage(Series series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return WantedLanguage(series.MediaType, series.OriginalLanguage, series.Language);
    }

    public Language WantedLanguage(Movie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        return WantedLanguage(MediaType.Movie, movie.OriginalLanguage, movie.Language);
    }

    // Unknown matches only untagged releases, so it is never a language to want (GET /languages hides it too).
    public static Language? EnsureWantable(Language? language) =>
        language == Language.Unknown ? throw new ArgumentException("Unknown is not a language you can ask for.", nameof(language)) : language;

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
        if (Movies is null || Series is null || Anime is null || DefaultLanguage is null || Exceptions is null || Exceptions.Any(rule => rule?.Language is null))
        {
            yield return new ValidationResult("movies, series, anime, defaultLanguage, exceptions and each exception's language are required.");
            yield break;
        }

        if (new[] { DefaultLanguage, Movies.Language, Series.Language, Anime.Language }.Concat(Exceptions.Select(rule => rule.Language)).Contains(Language.Unknown))
        {
            yield return new ValidationResult("Unknown is not a language you can ask for.");
        }

        foreach ((string name, MediaDefaults defaults) in new[] { ("movies", Movies), ("series", Series), ("anime", Anime) })
        {
            if (defaults.ProfileId < 1)
            {
                yield return new ValidationResult($"{name}: profileId must be a quality profile id.");
            }
        }

        if (Exceptions.Any(rule => rule.Category is null && rule.OriginalLanguage is null))
        {
            yield return new ValidationResult("An exception needs a category, an original language, or both.");
        }
    }
}
