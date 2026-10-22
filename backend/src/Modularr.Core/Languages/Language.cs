using System.Collections.Frozen;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Modularr.Core.Languages;

// Ids follow Radarr (the larger set); Sonarr-only languages are appended after it.
// The API and modularr.json name a language ("French"); the database stores its id (PersistenceJson, LanguageIdConverter).
[JsonConverter(typeof(LanguageNameJsonConverter))]
[TypeConverter(typeof(LanguageNameTypeConverter))]
public sealed record Language(int Id, string Name)
{
    public static readonly Language Original = new(-2, "Original");
    public static readonly Language Any = new(-1, "Any");
    public static readonly Language Unknown = new(0, "Unknown");
    public static readonly Language English = new(1, "English");
    public static readonly Language French = new(2, "French");
    public static readonly Language Spanish = new(3, "Spanish");
    public static readonly Language German = new(4, "German");
    public static readonly Language Italian = new(5, "Italian");
    public static readonly Language Danish = new(6, "Danish");
    public static readonly Language Dutch = new(7, "Dutch");
    public static readonly Language Japanese = new(8, "Japanese");
    public static readonly Language Icelandic = new(9, "Icelandic");
    public static readonly Language Chinese = new(10, "Chinese");
    public static readonly Language Russian = new(11, "Russian");
    public static readonly Language Polish = new(12, "Polish");
    public static readonly Language Vietnamese = new(13, "Vietnamese");
    public static readonly Language Swedish = new(14, "Swedish");
    public static readonly Language Norwegian = new(15, "Norwegian");
    public static readonly Language Finnish = new(16, "Finnish");
    public static readonly Language Turkish = new(17, "Turkish");
    public static readonly Language Portuguese = new(18, "Portuguese");
    public static readonly Language Flemish = new(19, "Flemish");
    public static readonly Language Greek = new(20, "Greek");
    public static readonly Language Korean = new(21, "Korean");
    public static readonly Language Hungarian = new(22, "Hungarian");
    public static readonly Language Hebrew = new(23, "Hebrew");
    public static readonly Language Lithuanian = new(24, "Lithuanian");
    public static readonly Language Czech = new(25, "Czech");
    public static readonly Language Hindi = new(26, "Hindi");
    public static readonly Language Romanian = new(27, "Romanian");
    public static readonly Language Thai = new(28, "Thai");
    public static readonly Language Bulgarian = new(29, "Bulgarian");
    public static readonly Language PortugueseBrazil = new(30, "Portuguese (Brazil)");
    public static readonly Language Arabic = new(31, "Arabic");
    public static readonly Language Ukrainian = new(32, "Ukrainian");
    public static readonly Language Persian = new(33, "Persian");
    public static readonly Language Bengali = new(34, "Bengali");
    public static readonly Language Slovak = new(35, "Slovak");
    public static readonly Language Latvian = new(36, "Latvian");
    public static readonly Language SpanishLatino = new(37, "Spanish (Latino)");
    public static readonly Language Catalan = new(38, "Catalan");
    public static readonly Language Croatian = new(39, "Croatian");
    public static readonly Language Serbian = new(40, "Serbian");
    public static readonly Language Bosnian = new(41, "Bosnian");
    public static readonly Language Estonian = new(42, "Estonian");
    public static readonly Language Tamil = new(43, "Tamil");
    public static readonly Language Indonesian = new(44, "Indonesian");
    public static readonly Language Telugu = new(45, "Telugu");
    public static readonly Language Macedonian = new(46, "Macedonian");
    public static readonly Language Slovenian = new(47, "Slovenian");
    public static readonly Language Malayalam = new(48, "Malayalam");
    public static readonly Language Kannada = new(49, "Kannada");
    public static readonly Language Albanian = new(50, "Albanian");
    public static readonly Language Afrikaans = new(51, "Afrikaans");
    public static readonly Language Marathi = new(52, "Marathi");
    public static readonly Language Tagalog = new(53, "Tagalog");
    public static readonly Language Urdu = new(54, "Urdu");
    public static readonly Language Romansh = new(55, "Romansh");
    public static readonly Language Mongolian = new(56, "Mongolian");
    public static readonly Language Georgian = new(57, "Georgian");
    public static readonly Language Azerbaijani = new(58, "Azerbaijani");
    public static readonly Language Uzbek = new(59, "Uzbek");
    public static readonly Language Malay = new(60, "Malay");
    public static readonly Language Irish = new(61, "Irish");

    public static readonly IReadOnlyList<Language> All =
    [
        Unknown, English, French, Spanish, German, Italian, Danish, Dutch, Japanese, Icelandic, Chinese, Russian,
        Polish, Vietnamese, Swedish, Norwegian, Finnish, Turkish, Portuguese, Flemish, Greek, Korean, Hungarian,
        Hebrew, Lithuanian, Czech, Hindi, Romanian, Thai, Bulgarian, PortugueseBrazil, Arabic, Ukrainian, Persian,
        Bengali, Slovak, Latvian, SpanishLatino, Catalan, Croatian, Serbian, Bosnian, Estonian, Tamil, Indonesian,
        Telugu, Macedonian, Slovenian, Malayalam, Kannada, Albanian, Afrikaans, Marathi, Tagalog, Urdu, Romansh,
        Mongolian, Georgian, Azerbaijani, Uzbek, Malay, Irish, Any, Original,
    ];

    private static readonly FrozenDictionary<int, Language> ById = All.ToFrozenDictionary(l => l.Id);

    private static readonly FrozenDictionary<string, Language> ByName =
        All.ToFrozenDictionary(l => l.Name, StringComparer.OrdinalIgnoreCase);

    public static Language FindById(int id) =>
        ById.TryGetValue(id, out Language? language)
            ? language
            : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown language id.");

    public static Language FindByName(string name) =>
        ByName.TryGetValue(name, out Language? language)
            ? language
            : throw new ArgumentException($"Unknown language '{name}'.", nameof(name));

    public override string ToString() => Name;
}

public sealed class LanguageNameJsonConverter : JsonConverter<Language>
{
    public override Language Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Language.FindByName(reader.GetString() ?? "");

    public override void Write(Utf8JsonWriter writer, Language value, JsonSerializerOptions options) => writer.WriteStringValue(value.Name);
}

// Lets the configuration binder read a language name from modularr.json.
public sealed class LanguageNameTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => Language.FindByName((string)value);
}
