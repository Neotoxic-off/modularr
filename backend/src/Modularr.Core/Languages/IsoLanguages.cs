using System.Collections.Frozen;

namespace Modularr.Core.Languages;

public sealed record IsoLanguage(string TwoLetterCode, string CountryCode, string ThreeLetterCode, Language Language);

public static class IsoLanguages
{
    // Order matters: the first entry wins for codes shared by several languages (nl, pt, es).
    private static readonly IsoLanguage[] All =
    [
        new("en", "", "eng", Language.English),
        new("fr", "fr", "fra", Language.French),
        new("es", "", "spa", Language.Spanish),
        new("de", "de", "deu", Language.German),
        new("it", "", "ita", Language.Italian),
        new("da", "", "dan", Language.Danish),
        new("nl", "", "nld", Language.Dutch),
        new("ja", "", "jpn", Language.Japanese),
        new("is", "", "isl", Language.Icelandic),
        new("zh", "cn", "zho", Language.Chinese),
        new("ru", "", "rus", Language.Russian),
        new("pl", "", "pol", Language.Polish),
        new("vi", "", "vie", Language.Vietnamese),
        new("sv", "", "swe", Language.Swedish),
        new("no", "", "nor", Language.Norwegian),
        new("nb", "", "nob", Language.Norwegian),
        new("fi", "", "fin", Language.Finnish),
        new("tr", "", "tur", Language.Turkish),
        new("pt", "pt", "por", Language.Portuguese),
        new("nl", "", "nld", Language.Flemish),
        new("el", "", "ell", Language.Greek),
        new("ko", "", "kor", Language.Korean),
        new("hu", "", "hun", Language.Hungarian),
        new("he", "", "heb", Language.Hebrew),
        new("cs", "", "ces", Language.Czech),
        new("hi", "", "hin", Language.Hindi),
        new("th", "", "tha", Language.Thai),
        new("bg", "", "bul", Language.Bulgarian),
        new("ro", "", "ron", Language.Romanian),
        new("pt", "br", "", Language.PortugueseBrazil),
        new("ar", "", "ara", Language.Arabic),
        new("uk", "", "ukr", Language.Ukrainian),
        new("fa", "", "fas", Language.Persian),
        new("bn", "", "ben", Language.Bengali),
        new("lt", "", "lit", Language.Lithuanian),
        new("sk", "", "slk", Language.Slovak),
        new("lv", "", "lav", Language.Latvian),
        new("es", "mx", "spa", Language.SpanishLatino),
        new("ca", "", "cat", Language.Catalan),
        new("hr", "", "hrv", Language.Croatian),
        new("sr", "", "srp", Language.Serbian),
        new("bs", "", "bos", Language.Bosnian),
        new("et", "", "est", Language.Estonian),
        new("ta", "", "tam", Language.Tamil),
        new("id", "", "ind", Language.Indonesian),
        new("te", "", "tel", Language.Telugu),
        new("mk", "", "mkd", Language.Macedonian),
        new("sl", "", "slv", Language.Slovenian),
        new("ml", "", "mal", Language.Malayalam),
        new("kn", "", "kan", Language.Kannada),
        new("sq", "", "sqi", Language.Albanian),
        new("af", "", "afr", Language.Afrikaans),
        new("mr", "", "mar", Language.Marathi),
        new("tl", "", "tgl", Language.Tagalog),
        new("ur", "", "urd", Language.Urdu),
        new("rm", "", "roh", Language.Romansh),
        new("mn", "", "mon", Language.Mongolian),
        new("ka", "", "kat", Language.Georgian),
        new("az", "", "aze", Language.Azerbaijani),
        new("uz", "", "uzb", Language.Uzbek),
        new("ms", "", "msa", Language.Malay),
        new("ga", "", "gle", Language.Irish),
    ];

    private static readonly FrozenDictionary<string, Language> AlternateCodes = new Dictionary<string, Language>
    {
        ["cn"] = Language.Chinese,
        ["yue"] = Language.Chinese,
        ["zhtw"] = Language.Chinese,
    }.ToFrozenDictionary();

    // ISO 639-2/B (bibliographic) codes mapped to their 639-2/T form.
    private static readonly FrozenDictionary<string, string> Iso639BToT = new Dictionary<string, string>
    {
        ["alb"] = "sqi",
        ["arm"] = "hye",
        ["baq"] = "eus",
        ["bur"] = "mya",
        ["chi"] = "zho",
        ["cze"] = "ces",
        ["dut"] = "nld",
        ["fre"] = "fra",
        ["geo"] = "kat",
        ["ger"] = "deu",
        ["gre"] = "ell",
        ["gsw"] = "deu",
        ["ice"] = "isl",
        ["mac"] = "mkd",
        ["mao"] = "mri",
        ["may"] = "msa",
        ["per"] = "fas",
        ["rum"] = "ron",
        ["slo"] = "slk",
        ["tib"] = "bod",
        ["wel"] = "cym",
        ["khk"] = "mon",
        ["mvf"] = "mon",
    }.ToFrozenDictionary();

    public static IsoLanguage? Find(string isoCode)
    {
        string[] parts = isoCode.ToLowerInvariant().Split('-');
        string code = parts[0];

        if (AlternateCodes.TryGetValue(isoCode.ToLowerInvariant(), out Language? alternate))
        {
            return Get(alternate);
        }

        if (code.Length == 2)
        {
            IsoLanguage[] matches = [.. All.Where(l => l.TwoLetterCode == code)];

            if (parts.Length > 1)
            {
                string country = parts[1];
                matches = matches.Any(l => l.CountryCode == country)
                    ? [.. matches.Where(l => l.CountryCode == country)]
                    : [.. matches.Where(l => l.CountryCode.Length == 0)];
            }

            return matches.FirstOrDefault();
        }

        if (code.Length == 3)
        {
            string terminologyCode = Iso639BToT.GetValueOrDefault(code, code);
            return All.FirstOrDefault(l => l.ThreeLetterCode == terminologyCode);
        }

        return null;
    }

    public static IsoLanguage? Get(Language language) => All.FirstOrDefault(l => l.Language == language);
}
