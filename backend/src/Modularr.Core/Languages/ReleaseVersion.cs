using System.Collections.Frozen;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Modularr.Core.Languages;

// The wanted version of a title: which audio (and for VOSTFR which subtitles) a release must carry. Besides the French scene
// versions, any other language is a version of its own ("German": a German audio track). French is VF and Original is VO, so
// no wish has two names; those two language names are still read as aliases (settings and columns from the language policy).
// The API, modularr.json and the database all store the name.
[JsonConverter(typeof(ReleaseVersionJsonConverter))]
[TypeConverter(typeof(ReleaseVersionTypeConverter))]
public sealed record ReleaseVersion
{
    public static readonly ReleaseVersion Any = new("Any", null);
    public static readonly ReleaseVersion VO = new("VO", null);
    public static readonly ReleaseVersion VF = new("VF", null);
    public static readonly ReleaseVersion VFQ = new("VFQ", null);
    public static readonly ReleaseVersion VOSTFR = new("VOSTFR", null);
    public static readonly ReleaseVersion Multi = new("MULTi", null);

    public static readonly IReadOnlyList<ReleaseVersion> All =
    [
        Any, VO, VF, VFQ, VOSTFR, Multi,
        .. Language.All.Where(l => l.Id > 0 && l != Language.French).Select(l => new ReleaseVersion(l.Name, l)),
    ];

    private static readonly FrozenDictionary<string, ReleaseVersion> ByName = All
        .Select(v => KeyValuePair.Create(v.Name, v))
        .Append(KeyValuePair.Create(Language.French.Name, VF))
        .Append(KeyValuePair.Create(Language.Original.Name, VO))
        .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private ReleaseVersion(string name, Language? audio)
    {
        Name = name;
        Audio = audio;
    }

    public string Name { get; }

    // Set only for a plain audio language version.
    public Language? Audio { get; }

    public static ReleaseVersion FindByName(string name) =>
        ByName.TryGetValue(name, out ReleaseVersion? version)
            ? version
            : throw new ArgumentException($"Unknown version '{name}'.", nameof(name));

    public bool Accepts(ReleaseLanguages release, Language originalLanguage)
    {
        ArgumentNullException.ThrowIfNull(release);
        bool original = release.HasAudio(originalLanguage, originalLanguage);
        bool french = release.HasAudio(Language.French, originalLanguage);

        return this switch
        {
            { Audio: Language audio } => release.HasAudio(audio, originalLanguage),
            _ when this == VO => original,
            // France French: a VFF/TRUEFRENCH/FRENCH track, or a French track that is not only the Quebec dub (a French film).
            _ when this == VF => french && (release.FranceFrench || !release.QuebecFrench),
            _ when this == VFQ => release.QuebecFrench,
            _ when this == VOSTFR => original && release.Subtitles.Contains(Language.French),
            _ when this == Multi => original && french && release.Audio.Count > 1,
            _ => true,
        };
    }

    public override string ToString() => Name;
}

public sealed class ReleaseVersionJsonConverter : JsonConverter<ReleaseVersion>
{
    public override ReleaseVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ReleaseVersion.FindByName(reader.GetString() ?? "");

    public override void Write(Utf8JsonWriter writer, ReleaseVersion value, JsonSerializerOptions options) => writer.WriteStringValue(value.Name);
}

// Lets the configuration binder read a version name from modularr.json.
public sealed class ReleaseVersionTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => ReleaseVersion.FindByName((string)value);
}
