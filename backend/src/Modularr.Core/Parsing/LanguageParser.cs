using System.Globalization;
using System.Text.RegularExpressions;
using Modularr.Core.Languages;

namespace Modularr.Core.Parsing;

public static partial class LanguageParser
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private const string LanguagesOnly =
        @"(?<english>\b(?:ing|eng)\b)|(?<italian>\b(?:ita|italian)\b)|(?<german>(?:swiss)?german\b|videomann|ger[. ]dub|\bger\b)|" +
        @"(?<flemish>flemish)|(?<greek>greek)|(?<french>(?<!(?:VOST|SUB)[-_. ]?)(?:_|\b)(?:FR|VF|VF2|VFF|VFI|TRUEFRENCH|FRENCH|FRE|FRA)(?:_|\b))|(?<quebec>(?:_|\b)VFQ(?:_|\b))|" +
        @"(?<russian>\b(?:rus|ru)\b)|(?<hungarian>\b(?:HUNDUB|HUN)\b)|(?<hebrew>\bHebDub(?:bed)?\b)|" +
        @"(?<polish>\b(?:PL\W?DUB|DUB\W?PL|LEK\W?PL|PL\W?LEK)\b)|(?<chinese>\[(?:CH[ST]|BIG5|GB)\]|简|繁|字幕|国语音轨[.+])|" +
        @"(?<bulgarian>\bbgaudio\b)|(?<spanish>\b(?:español|castellano|esp|spa(?!\(Latino\)))\b)|(?<ukrainian>\b(?:\dx?)?ukr)|" +
        @"(?<romanian>\b(?:RoDubbed|ROMANIAN)\b)|(?<thai>\bTHAI\b)|(?<catalan>[-,. ]cat[. ](?:DD|subs)|\b(?:catalan?|catalán|català)\b)|(?<latvian>\b(?:lat|lav|lv)\b)|" +
        @"(?<turkish>\btur\b)|(?<urdu>\burdu\b)|(?<romansh>\b(?:romansh|rumantsch|romansch)\b)|(?<mongolian>\b(?:mongolian|khalkha)\b)|" +
        @"(?<georgian>\b(?:geo|ka|kat|georgian)\b)|(?<irish>\b(?:irish|gaeilge)\b)|(?<japanese>\(JA\)|JAP|JPN)|(?<korean>\bKOR\b)|" +
        @"(?<vietnamese>\bVIE\b)|(?<telugu>\btel\b)|(?<portuguese>[_. ]por[_. ])|(?<brazilian>\b(?:dublado|pt-BR)\b)|(?<original>\b(?:orig|original)\b|(?:_|\b)VO(?:_|\b))";

    // A language tag followed (possibly through other language tags) by "sub" names a subtitle, not an audio track.
    [GeneratedRegex($"(?:{LanguagesOnly})(?!(?:[-_. ](?:{LanguagesOnly}))*[-_. ]subs?)", Options)]
    private static partial Regex LanguageRegex { get; }

    // The same tags when they name a subtitle ("French.Subs", "ENG.FRE.SUBS").
    [GeneratedRegex($"(?:{LanguagesOnly})(?=(?:[-_. ](?:{LanguagesOnly}))*[-_. ]subs?)", Options)]
    private static partial Regex SubtitleLanguageTagRegex { get; }

    // VOSTFR / SUBFRENCH: original audio with French subtitles; VOSTA / VOSTENG / SUBENG: with English subtitles.
    [GeneratedRegex(@"\b(?:(?<french>VOST[-_. ]?FR|STFR|SUB[-_. ]?FR(?:ENCH)?)|(?<english>VOSTA|VOST[-_. ]?ENG?|SUB[-_. ]?ENG(?:LISH)?))\b", Options)]
    private static partial Regex SubtitleTagRegex { get; }

    [GeneratedRegex(@"\bMULTI[-_. ]?SUBS?\b", Options)]
    private static partial Regex MultiSubsRegex { get; }

    // Subtitles burned into the picture.
    [GeneratedRegex(@"\bHARD[-_. ]?SUB(?:S|BED)?\b|\bHC\b", Options)]
    private static partial Regex HardsubRegex { get; }

    [GeneratedRegex(@"(?:(?i)(?<!SUB[\W|_|^]))(?:(?<english>\bEN\b)|(?<lithuanian>\bLT\b)|(?<czech>\bCZ\b)|(?<polish>\bPL\b)|(?<bulgarian>\bBG\b)|(?<slovak>\bSK\b)|(?<german>\bDE\b)|(?<spanish>\b(?<!DTS[._ -])ES\b))(?:(?i)(?![\W|_|^]SUB))", RegexOptions.CultureInvariant)]
    private static partial Regex CaseSensitiveLanguageRegex { get; }

    // The series title can name a language ("The Spanish Princess"); only what follows SxxEyy describes the release.
    [GeneratedRegex(@".*?[_. ](S\d{2}(?:E\d{2,4})*[_. ].*)", Options)]
    private static partial Regex CleanSeriesTitleRegex { get; }

    [GeneratedRegex(@"(?<!WEB[-_. ]?)\bDL\b", Options)]
    private static partial Regex GermanDualLanguageRegex { get; }

    [GeneratedRegex(@"\bML\b", Options)]
    private static partial Regex GermanMultiLanguageRegex { get; }

    // "Multi Subs" names subtitles.
    [GeneratedRegex(@"\bMULTI\b(?![-_. ]?SUBS?\b)", Options)]
    private static partial Regex MultiAudioRegex { get; }

    [GeneratedRegex(".+?([-_. ](?<tags>forced|foreign|default|cc|psdh|sdh))*[-_. ](?<iso_code>[a-z]{2,3})([-_. ](?<tags>forced|foreign|default|cc|psdh|sdh))*$", Options)]
    private static partial Regex SubtitleLanguageRegex { get; }

    [GeneratedRegex(@".+?(\.((?<tags1>forced|foreign|default|cc|psdh|sdh)|(?<iso_code>[a-z]{2,3})))*[-_. ](?<title>[^.]*)(\.((?<tags2>forced|foreign|default|cc|psdh|sdh)|(?<iso_code>[a-z]{2,3})))*$", Options)]
    private static partial Regex SubtitleLanguageTitleRegex { get; }

    [GeneratedRegex(@"^((?<title>.+) - )?(?<copy>(?<!\d+)\d{1,3}(?!\d+))$", RegexOptions.CultureInvariant)]
    private static partial Regex SubtitleTitleRegex { get; }

    private static readonly (string Keyword, Language Language)[] Keywords =
    [
        ("spanish", Language.Spanish), ("danish", Language.Danish), ("dutch", Language.Dutch),
        ("japanese", Language.Japanese), ("icelandic", Language.Icelandic), ("mandarin", Language.Chinese),
        ("cantonese", Language.Chinese), ("chinese", Language.Chinese), ("korean", Language.Korean),
        ("russian", Language.Russian), ("polish", Language.Polish), ("vietnamese", Language.Vietnamese),
        ("swedish", Language.Swedish), ("norwegian", Language.Norwegian), ("finnish", Language.Finnish),
        ("turkish", Language.Turkish), ("portuguese", Language.Portuguese), ("hungarian", Language.Hungarian),
        ("hebrew", Language.Hebrew), ("arabic", Language.Arabic), ("hindi", Language.Hindi),
        ("malayalam", Language.Malayalam), ("ukrainian", Language.Ukrainian), ("bulgarian", Language.Bulgarian),
        ("slovak", Language.Slovak), ("brazilian", Language.PortugueseBrazil), ("latino", Language.SpanishLatino),
        ("latvian", Language.Latvian), ("azerbaijan", Language.Azerbaijani), ("uzbek", Language.Uzbek),
        ("persian", Language.Persian),
        ("bengali", Language.Bengali), ("tamil", Language.Tamil), ("telugu", Language.Telugu),
        ("kannada", Language.Kannada), ("albanian", Language.Albanian), ("afrikaans", Language.Afrikaans),
        ("marathi", Language.Marathi), ("tagalog", Language.Tagalog),
    ];

    private static readonly (string Group, Language Language)[] Groups =
    [
        ("english", Language.English), ("italian", Language.Italian), ("german", Language.German),
        ("flemish", Language.Flemish), ("greek", Language.Greek), ("french", Language.French), ("quebec", Language.French),
        ("russian", Language.Russian), ("hungarian", Language.Hungarian), ("hebrew", Language.Hebrew),
        ("polish", Language.Polish), ("chinese", Language.Chinese), ("bulgarian", Language.Bulgarian),
        ("spanish", Language.Spanish), ("ukrainian", Language.Ukrainian), ("romanian", Language.Romanian),
        ("catalan", Language.Catalan), ("latvian", Language.Latvian), ("turkish", Language.Turkish),
        ("urdu", Language.Urdu), ("romansh", Language.Romansh), ("mongolian", Language.Mongolian),
        ("georgian", Language.Georgian), ("irish", Language.Irish), ("japanese", Language.Japanese),
        ("korean", Language.Korean), ("vietnamese", Language.Vietnamese), ("telugu", Language.Telugu),
        ("portuguese", Language.Portuguese), ("brazilian", Language.PortugueseBrazil), ("original", Language.Original),
        ("lithuanian", Language.Lithuanian), ("czech", Language.Czech), ("slovak", Language.Slovak),
        ("thai", Language.Thai),
    ];

    public static IReadOnlyList<Language> ParseLanguages(string title) => ParseAudio(title).Audio;

    // Audio and subtitles apart, with the French dub variant the version policy needs (VF vs VFQ).
    public static ReleaseLanguages ParseReleaseLanguages(string title)
    {
        // A release group can read as a language tag ("-FRA").
        if (ReleaseGroupParser.ParseReleaseGroup(title) is string group)
        {
            title = title.Replace(group, "RlsGrp", StringComparison.Ordinal);
        }

        (IReadOnlyList<Language> audio, bool franceFrench, bool quebecFrench) = ParseAudio(title);
        title = ReleaseTokens(title);
        IEnumerable<Language> subtitles = SubtitleTagRegex.Matches(title).Concat(SubtitleLanguageTagRegex.Matches(title))
            .SelectMany(match => Groups.Where(g => match.Groups[g.Group].Success).Select(g => g.Language))
            .Where(l => l.Id > 0);

        return new ReleaseLanguages(audio)
        {
            FranceFrench = franceFrench,
            QuebecFrench = quebecFrench,
            Subtitles = [.. subtitles.Distinct()],
            MultiSubs = MultiSubsRegex.IsMatch(title),
            Hardsub = HardsubRegex.IsMatch(title),
        };
    }

    private static string ReleaseTokens(string title)
    {
        Match seriesMatch = CleanSeriesTitleRegex.Match(title);
        return seriesMatch.Success ? seriesMatch.Result("$1") : title;
    }

    private static (IReadOnlyList<Language> Audio, bool FranceFrench, bool QuebecFrench) ParseAudio(string title)
    {
        title = ReleaseTokens(title);
        bool franceFrench = false;
        bool quebecFrench = false;

        List<Language> languages = [.. Keywords.Where(k => title.Contains(k.Keyword, StringComparison.OrdinalIgnoreCase)).Select(k => k.Language)];

        foreach (Match match in CaseSensitiveLanguageRegex.Matches(title).Concat(LanguageRegex.Matches(title)))
        {
            languages.AddRange(Groups.Where(g => match.Groups[g.Group].Success).Select(g => g.Language));
            franceFrench |= match.Groups["french"].Success;
            quebecFrench |= match.Groups["quebec"].Success;
        }

        if (title.Contains("english", StringComparison.OrdinalIgnoreCase))
        {
            languages.Add(Language.English);
        }

        // MULTi: several audio tracks, the original among them.
        // shortcut: a bare MULTi is read as French + original (the French scene convention), add a per-indexer language when
        // non-French trackers tag MULTi without naming the dub.
        if (MultiAudioRegex.IsMatch(title))
        {
            franceFrench |= languages.Count == 0;
            languages.AddRange(languages.Count == 0 ? [Language.French, Language.Original] : [Language.Original]);
        }

        if (languages.Count == 0)
        {
            return ([Language.Unknown], false, false);
        }

        // German releases tag the extra audio tracks: DL adds the original, ML adds the original and English.
        if (languages.Count == 1 && languages[0] == Language.German)
        {
            if (GermanDualLanguageRegex.IsMatch(title))
            {
                languages.Add(Language.Original);
            }
            else if (GermanMultiLanguageRegex.IsMatch(title))
            {
                languages.AddRange([Language.Original, Language.English]);
            }
        }

        return ([.. languages.Distinct()], franceFrench, quebecFrench);
    }

    public static IReadOnlyList<string> ParseLanguageTags(string fileName)
    {
        Match match = SubtitleLanguageRegex.Match(Path.GetFileNameWithoutExtension(fileName));
        return [.. match.Groups["tags"].Captures.Where(c => c.Value.Length > 0).Select(c => c.Value.ToLowerInvariant())];
    }

    public static Language ParseSubtitleLanguage(string fileName)
    {
        string simpleFileName = Path.GetFileNameWithoutExtension(fileName);
        Match match = SubtitleLanguageRegex.Match(simpleFileName);

        if (match.Success)
        {
            return IsoLanguages.Find(match.Groups["iso_code"].Value)?.Language ?? Language.Unknown;
        }

        return Language.All.FirstOrDefault(l => simpleFileName.EndsWith(l.Name, StringComparison.OrdinalIgnoreCase)) ?? Language.Unknown;
    }

    public static SubtitleTitleInfo ParseBasicSubtitle(string fileName) =>
        new(ParseSubtitleLanguage(fileName), ParseLanguageTags(fileName));

    public static SubtitleTitleInfo ParseSubtitleLanguageInformation(string fileName)
    {
        Match match = SubtitleLanguageTitleRegex.Match(Path.GetFileNameWithoutExtension(fileName));

        if (!match.Groups["title"].Success || match.Groups["iso_code"].Captures.Count != 1)
        {
            return ParseBasicSubtitle(fileName);
        }

        Language language = IsoLanguages.Find(match.Groups["iso_code"].Value)?.Language ?? Language.Unknown;
        string[] tags =
        [
            .. match.Groups["tags1"].Captures.Concat(match.Groups["tags2"].Captures)
                .Where(c => c.Value.Length > 0)
                .Select(c => c.Value.ToLowerInvariant()),
        ];
        string rawTitle = match.Groups["title"].Value;
        Match titleMatch = SubtitleTitleRegex.Match(rawTitle);

        return new SubtitleTitleInfo(language, tags)
        {
            RawTitle = rawTitle,
            Title = !titleMatch.Success ? rawTitle : titleMatch.Groups["title"].Success ? titleMatch.Groups["title"].Value : null,
            Copy = titleMatch.Success ? int.Parse(titleMatch.Groups["copy"].Value, CultureInfo.InvariantCulture) : 0,
            TitleFirst = match.Groups["tags1"].Captures.Count == 0,
        };
    }
}
