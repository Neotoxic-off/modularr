using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Modularr.Core.MediaFiles;

namespace Modularr.Core.Parsing;

// Release title helpers shared by the episode and movie parsers (Sonarr/Radarr ParserCommon, FileExtensions, CleanSeriesTitle).
public static partial class ReleaseTitle
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Korean series without season number, replace with S01Exxx and remove airdate
    [GeneratedRegex(@"\.E(\d{2,4})\.\d{6}\.(.*-(F1RST|NEXT))$")]
    private static partial Regex KoreanEpisodeRegex { get; }

    // Some Chinese anime releases contain both English and Chinese titles, remove the Chinese title and replace with normal anime pattern
    [GeneratedRegex(@"^\[(?:(?<subgroup>[^\]]+?)(?:[一-鿌]+)?)\]\[(?<title>[^\]]+?)(?:\s(?<chinesetitle>[一-鿌][^\]]*?))\]\[(?:(?:[一-鿌]+?)?(?<episode>\d{1,4})(?:[一-鿌]+?)?)\]")]
    private static partial Regex ChineseEnglishTitleRegex { get; }

    // Chinese LoliHouse/ZERO/Lilith-Raws/Skymoon-Raws/orion origin releases don't use the expected brackets, normalize using brackets
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]*?(?:LoliHouse|ZERO|Lilith-Raws|Skymoon-Raws|orion origin)[^\]]*?)\](?<title>[^\[\]]+?)(?: - (?<episode>[0-9-]+)\s*|\[第?(?<episode>[0-9]+(?:-[0-9]+)?)话?(?:END|完)?\])\[")]
    private static partial Regex ChineseUnbracketedGroupRegex { get; }

    // Most Chinese anime releases contain additional brackets/separators for chinese and non-chinese titles, remove junk first and if it has S0x as season number, convert it to Sx
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s?★[^\[ -]+\s?)?\[?(?:(?<chinesetitle>(?=[^\]]*?[一-鿌])[^\]]*?)(?:\]\[|\s*[_/·]\s*)){0,2}(?<title>[^\[\]]+?)(?:\s(?:S?(?<!\d+)((0)(?<season>\d)|(?<season>[1-9]\d))(?!\d+)))\]?(?:\[\d{4}\])?\[第?(?<episode>[0-9]+(?:-[0-9]+)?)(?:话|集)?(?: ?END|完| ?Fin)?\]")]
    private static partial Regex ChineseSeasonEpisodeRegex { get; }

    // Some Chinese releases don't include a separation between Chinese and English titles within the same bracketed group
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\]\[(?<chinesetitle>(?<![^a-zA-Z0-9])[^a-zA-Z0-9]+)(?<title>[^\]]+?)\](?:\[\d{4}\])?\[第?(?<episode>[0-9]+(?:-[0-9]+)?)(?:话|集)?(?: ?END|完| ?Fin)?\]")]
    private static partial Regex ChineseGluedTitleRegex { get; }

    // Chinese season packs, that may not actually have the full season so treated as multi-episode
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s?★[^\[ -]+\s?)?\[?(?:(?<chinesetitle>(?=[^\]]*?[一-鿌])[^\]]*?)(?:\]\[|\s*[_/·]\s*)){0,2}(?<title>[^\]]+?)\]?(?:\[\d{4}\])?\[(?:第|全)?(?<episode>[0-9]{1,4}-[0-9]{1,4})(?:话|集)?\](?:\[(?<language>.+?)\+.+?\])[_. ](?<title>.+?)S(?<season>[0-9]{1,2})(?<rest>.+?)$")]
    private static partial Regex ChinesePartialSeasonPackRegex { get; }

    // Chinese season packs, that actually have the full season so treated as multi-episode
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s?★[^\[ -]+\s?)?\[?(?:(?<chinesetitle>(?=[^\]]*?[一-鿌])[^\]]*?)(?:\]\[|\s*[_/·]\s*)){0,2}(?<title>[^\]]+?)\]?(?:\[\d{4}\])?\[(?:全)?(?<episode>[0-9]{1,4})(?:话|集)?\](?:\[(?<language>.+?)\+.+?\])[_. ](?<title>.+?)S(?<season>[0-9]{1,2})(?<rest>.+?)$")]
    private static partial Regex ChineseFullSeasonPackRegex { get; }

    // Most Chinese anime releases contain additional brackets/separators for chinese and non-chinese titles, remove junk and replace with normal anime pattern
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s?★[^\[ -]+\s?)?\[?(?:(?<chinesetitle>(?=[^\]]*?[一-鿌])[^\]]*?)(?:\]\[|\s*[_/·]\s*)){0,2}(?<title>[^\]]+?)\]?(?:\[\d{4}\])?\[第?(?<episode>[0-9]{1,4}(?:-[0-9]{1,4})?)(?:话|集)?(?: ?END|完| ?Fin)?\]")]
    private static partial Regex ChineseBracketedRegex { get; }

    // Some Chinese anime releases contain both Chinese and English titles, remove the Chinese title first and if it has S0x as season number, convert it to Sx
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s)(?:(?<chinesetitle>(?=[^\]]*?[一-鿌])[^\]]*?)(?:\s/\s))(?<title>[^\[\]]+?)(?:\s(?:S?(?<!\d+)((0)(?<season>\d)|(?<season>[1-9]\d))(?!\d+)))(?:[- ]+)(?<episode>[0-9]+(?:-[0-9]+)?)话?(?:END|完)?")]
    private static partial Regex ChineseSlashSeasonRegex { get; }

    // Some Chinese anime releases contain both English and Chinese titles, remove the Chinese title and replace with normal anime pattern
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s)(?:(?<title>[^\]]+?)(?:\s/\s))(?<chinesetitle>(?=[^\]]*?[一-鿌])[^\]]*?)(?:[- ]+)(?<episode>[0-9]+(?:-[0-9]+)?(?![a-z]))话?(?:END|完)?")]
    private static partial Regex EnglishSlashChineseRegex { get; }

    // Some Chinese anime releases contain both Chinese and English titles, remove the Chinese title and replace with normal anime pattern
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s)(?:(?<chinesetitle>(?=[^\]]*?[一-鿌])[^\]]*?)(?:\s/\s))(?<title>[^\]]+?)(?:[- ]+)(?<episode>[0-9]+(?:-[0-9]+)?(?![a-z]))话?(?:END|完)?")]
    private static partial Regex ChineseSlashEnglishRegex { get; }

    // GM-Team releases with lots of square brackets
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:(?<chinesubgroup>\[(?=[^\]]*?[一-鿌])[^\]]*\])+)\[(?<title>[^\]]+?)\](?<junk>\[[^\]]+\])*\[(?<episode>[0-9]+(?:-[0-9]+)?)( END| Fin)?\]")]
    private static partial Regex GmTeamRegex { get; }

    // Some Chinese anime releases contain both Chinese and English titles separated by | instead of /, remove the Chinese title and replace with normal anime pattern
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s)(?:(?<chinesetitle>(?=[^\]]*?[一-鿌])[^\]]*?)(?:\s\|\s))(?<title>[^\]]+?)(?:[- ]+)(?<episode>[0-9]+(?:-[0-9]+)?(?![a-z]))话?(?:END|完)?")]
    private static partial Regex ChinesePipeEnglishRegex { get; }

    // Spanish releases with information in brackets
    [GeneratedRegex(@"^(?<title>.+?(?=[ ._-]\()).+?\((?<year>\d{4})\/(?<info>S[^\/]+)")]
    private static partial Regex SpanishBracketInfoRegex { get; }

    private static readonly (Regex Regex, string Replacement)[] PreSubstitutions =
    [
        (KoreanEpisodeRegex, ".S01E$1.$2"),
        (ChineseEnglishTitleRegex, "[${subgroup}] ${title} - ${episode} - "),
        (ChineseUnbracketedGroupRegex, "[${subgroup}][${title}][${episode}]["),
        (ChineseSeasonEpisodeRegex, "[${subgroup}] ${title} S${season} - ${episode} "),
        (ChineseGluedTitleRegex, "[${subgroup}] ${title} - ${episode} "),
        (ChinesePartialSeasonPackRegex, "${title}S${season}E${episode}.${language}${rest}"),
        (ChineseFullSeasonPackRegex, "${title}S${season}.${language}${rest}"),
        (ChineseBracketedRegex, "[${subgroup}] ${title} - ${episode} "),
        (ChineseSlashSeasonRegex, "[${subgroup}] ${title} S${season} - ${episode} "),
        (EnglishSlashChineseRegex, "[${subgroup}] ${title} - ${episode} "),
        (ChineseSlashEnglishRegex, "[${subgroup}] ${title} - ${episode} "),
        (GmTeamRegex, "[${subgroup}] ${title} - ${episode} "),
        (ChinesePipeEnglishRegex, "[${subgroup}] ${title} - ${episode} "),
        (SpanishBracketInfoRegex, "${title} (${year}) - ${info} "),
    ];

    [GeneratedRegex(@"^(?:(?:\[|\()\s*)?(?:www\.)?[-a-z0-9-]{1,256}\.(?<!Naruto-Kun\.)(?:[a-z]{2,6}\.[a-z]{2,6}|xn--[a-z0-9-]{4,}|[a-z]{2,})\b(?:\s*(?:\]|\))|[ -]{2,})[ -]*", Options)]
    private static partial Regex WebsitePrefixRegex { get; }

    [GeneratedRegex(@"(?:\[\s*)?(?:www\.)?[-a-z0-9-]{1,256}\.(?:xn--[a-z0-9-]{4,}|[a-z]{2,6})\b(?:\s*\])$", Options)]
    private static partial Regex WebsitePostfixRegex { get; }

    [GeneratedRegex(@"\[(?:ettv|rartv|rarbg|cttv|publichd)\]$", Options)]
    private static partial Regex TorrentSuffixRegex { get; }

    [GeneratedRegex(@"\.[a-z0-9]{2,4}$", Options)]
    private static partial Regex FileExtensionRegex { get; }

    private static readonly FrozenSet<string> UsenetExtensions = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, ".par2", ".nzb");

    private static readonly Regex[] HashedReleaseRegexes =
    [
        Md5HashRegex, ShortLowerHashRegex, NzbGeekUpperRegex, NzbGeekLowerRegex, BackupFileRegex, OneTwoThreeRegex,
        AbcRegex, AbcXyzRegex, B00bsRegex, DateUnderscoreRegex, Hash30Regex, Hash26Regex, Hash39Regex, Hash24Regex,
    ];

    // Generic match for md5 and mixed-case hashes.
    [GeneratedRegex(@"^[0-9a-zA-Z]{32}")]
    private static partial Regex Md5HashRegex { get; }

    [GeneratedRegex(@"^[a-z0-9]{24}$")]
    private static partial Regex ShortLowerHashRegex { get; }

    // NZBGeek format, kept strict because it is close to valid 101 episode numbering.
    [GeneratedRegex(@"^[A-Z]{11}\d{3}$")]
    private static partial Regex NzbGeekUpperRegex { get; }

    [GeneratedRegex(@"^[a-z]{12}\d{3}$")]
    private static partial Regex NzbGeekLowerRegex { get; }

    [GeneratedRegex(@"^Backup_\d{5,}S\d{2}-\d{2}$")]
    private static partial Regex BackupFileRegex { get; }

    [GeneratedRegex(@"^123$")]
    private static partial Regex OneTwoThreeRegex { get; }

    [GeneratedRegex(@"^abc$", Options)]
    private static partial Regex AbcRegex { get; }

    [GeneratedRegex(@"^abc[-_. ]xyz", Options)]
    private static partial Regex AbcXyzRegex { get; }

    [GeneratedRegex(@"^b00bs$", Options)]
    private static partial Regex B00bsRegex { get; }

    [GeneratedRegex(@"^\d{6}_\d{2}$")]
    private static partial Regex DateUnderscoreRegex { get; }

    [GeneratedRegex(@"^[0-9a-zA-Z]{30}")]
    private static partial Regex Hash30Regex { get; }

    [GeneratedRegex(@"^[0-9a-zA-Z]{26}")]
    private static partial Regex Hash26Regex { get; }

    [GeneratedRegex(@"^[0-9a-zA-Z]{39}")]
    private static partial Regex Hash39Regex { get; }

    [GeneratedRegex(@"^[0-9a-zA-Z]{24}")]
    private static partial Regex Hash24Regex { get; }

    [GeneratedRegex(@"((?:\b|_)(?<!^)([aà](?!$)|an|the|and|or|of)(?!$)(?:\b|_))|\W|_", Options)]
    private static partial Regex NormalizeRegex { get; }

    [GeneratedRegex(@"(?<=\b\d+)%")]
    private static partial Regex PercentRegex { get; }

    // Letters that do not decompose into base letter + accent under Unicode normalization.
    private static readonly FrozenDictionary<char, string> UndecomposableLetters = new Dictionary<char, string>
    {
        ['ð'] = "d", ['đ'] = "d", ['ø'] = "o", ['ł'] = "l", ['ı'] = "i", ['æ'] = "ae", ['œ'] = "oe", ['ß'] = "ss", ['þ'] = "th",
    }.ToFrozenDictionary();

    public static string RemoveFileExtension(string title) =>
        FileExtensionRegex.Replace(title, m => MediaFileExtensions.IsMediaFile(m.Value) || UsenetExtensions.Contains(m.Value) ? "" : m.Value);

    // Titles made of a hash or an indexer placeholder instead of a release name.
    public static bool IsHashed(string titleWithoutExtension) => HashedReleaseRegexes.Any(r => r.IsMatch(titleWithoutExtension));

    public static string RemoveWebsites(string title) =>
        TorrentSuffixRegex.Replace(WebsitePostfixRegex.Replace(WebsitePrefixRegex.Replace(title, ""), ""), "");

    // Lower-case, no punctuation, no inner articles, no accents: the key titles are matched on.
    public static string CleanTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || long.TryParse(title, CultureInfo.InvariantCulture, out _))
        {
            return title;
        }

        return RemoveDiacritics(NormalizeRegex.Replace(PercentRegex.Replace(title, "percent"), "").ToLowerInvariant());
    }

    internal static string PreSubstitute(string title)
    {
        foreach ((Regex regex, string replacement) in PreSubstitutions)
        {
            title = regex.Replace(title, replacement);
        }

        return title;
    }

    internal static string PreSubstituteFirst(string title)
    {
        foreach ((Regex regex, string replacement) in PreSubstitutions)
        {
            if (regex.IsMatch(title))
            {
                return regex.Replace(title, replacement);
            }
        }

        return title;
    }

    internal static string StripWebsitePrefix(string title) => WebsitePrefixRegex.Replace(title, "");

    internal static string StripTorrentSuffix(string title) => TorrentSuffixRegex.Replace(title, "");

    // shortcut: FormD plus a small table instead of the full Diacritical.Net map, extend the table when a title shows up unmatched.
    private static string RemoveDiacritics(string text)
    {
        StringBuilder builder = new(text.Length);
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (UndecomposableLetters.TryGetValue(c, out string? replacement))
            {
                builder.Append(replacement);
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
