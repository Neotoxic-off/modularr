namespace Modularr.Core.Languages;

// What a release title says about its tracks (LanguageParser.ParseReleaseLanguages). Audio is Unknown when untagged and holds
// Original for an original track (MULTi, VO); FranceFrench/QuebecFrench tell which French dub it carries (VFF/TRUEFRENCH vs VFQ).
public sealed record ReleaseLanguages(IReadOnlyList<Language> Audio)
{
    public bool FranceFrench { get; init; }
    public bool QuebecFrench { get; init; }

    // Named subtitle languages (VOSTFR, SUBFRENCH, VOSTA, "French.Subs"); MultiSubs: several unnamed ones; Hardsub: burned in.
    public IReadOnlyList<Language> Subtitles { get; init; } = [];
    public bool MultiSubs { get; init; }
    public bool Hardsub { get; init; }

    public static ReleaseLanguages Untagged { get; } = new([Language.Unknown]);

    // An untagged release, and the Original track of a multi-audio one, are in the original language.
    public bool HasAudio(Language language, Language originalLanguage) =>
        Audio.Any(a => (a == Language.Unknown || a == Language.Original ? originalLanguage : a) == language);

    public override string ToString() =>
        Subtitles.Count == 0 ? string.Join(", ", Audio) : $"{string.Join(", ", Audio)} (subtitles: {string.Join(", ", Subtitles)})";
}
