using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Modularr.Core.Qualities;

namespace Modularr.Core.Parsing.Movies;

public static partial class MovieTitleParser
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private const string EditionPattern = @"\(?\b(?<edition>(((Recut.|Extended.|Ultimate.)?(Director.?s|Collector.?s|Theatrical|Ultimate|Extended|Despecialized|(Special|Rouge|Final|Assembly|Imperial|Diamond|Signature|Hunter|Rekall)(?=(.(Cut|Edition|Version)))|\d{2,3}(th)?.Anniversary)(?:.(Cut|Edition|Version))?(.(Extended|Uncensored|Remastered|Unrated|Uncut|Open.?Matte|IMAX|Fan.?Edit))?|((Uncensored|Remastered|Unrated|Uncut|Open?.Matte|IMAX|Fan.?Edit|Restored|((2|3|4)in1))))))\b\)?";

    [GeneratedRegex(@"^.+?" + EditionPattern, Options)]
    private static partial Regex ReportEditionRegex { get; }

    [GeneratedRegex(@"\b((?<hcsub>(\w+(?<!SOFT|MULTI|HORRIBLE)SUBS?))|(?<hc>(HC|SUBBED)))\b", Options)]
    private static partial Regex HardcodedSubsRegex { get; }

    // Anime [Subgroup] with year in parentheses, before the generic anime+year one so a year inside the title survives.
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)\s*\((?<year>(1(8|9)|20)\d{2})\).*?(?<hash>\[\w{8}\])?(?:$|\.)", Options)]
    private static partial Regex AnimeYearInParenthesesRegex { get; }

    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|x|\d+|\]|\W\d+)))+.*?(?<hash>\[\w{8}\])?(?:$|\.)", Options)]
    private static partial Regex AnimeYearRegex { get; }

    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)((v)(?:\d{1,2})(?:([-_. ])))(\[.*)?(?:[\[(][^])])?.*?(?<hash>\[\w{8}\])(?:$|\.)", Options)]
    private static partial Regex AnimeVersionedRegex { get; }

    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)(\[.*).*?(?<hash>\[\w{8}\])(?:$|\.)", Options)]
    private static partial Regex AnimeDoubleBracketsRegex { get; }

    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+)(?:[\[(][^])]).*?(?<hash>\[\w{8}\])(?:$|\.)", Options)]
    private static partial Regex AnimeBracketsRegex { get; }

    // German and TrueFrench tracker formats (missing or misplaced year).
    [GeneratedRegex(@"^(?<title>(?![(\[]).+?)((\W|_))(" + EditionPattern + @".{1,3})?(?:(?<!(19|20)\d{2}.*?)(?<!(?:Good|The)[_ .-])(German|TrueFrench))(.+?)(?=((19|20)\d{2}|$))(?<year>(19|20)\d{2}(?!p|i|\d+|\]|\W\d+))?(\W+|_|$)(?!\\)", Options)]
    private static partial Regex GermanRegex { get; }

    // Edition before the year, e.g. Mission.Impossible.3.Special.Edition.2011
    [GeneratedRegex(@"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*" + EditionPattern + @".{1,3}(?<year>(1(8|9)|20)\d{2}(?!p|i|\d+|\]|\W\d+)))+(\W+|_|$)(?!\\)", Options)]
    private static partial Regex EditionBeforeYearRegex { get; }

    [GeneratedRegex(@"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|(1(8|9)|20)\d{2}|\]|\W(1(8|9)|20)\d{2})))+(\W+|_|$)(?!\\)", Options)]
    private static partial Regex NormalRegex { get; }

    // PassThePopcorn torrent names: Star.Wars[PassThePopcorn]
    [GeneratedRegex(@"^(?<title>.+?)?(?:(?:[-_\W](?<![()\[!]))*(?<year>(\[\w *\])))+(\W+|_|$)(?!\\)", Options)]
    private static partial Regex PassThePopcornRegex { get; }

    [GeneratedRegex(@"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|\d+|\W\d+)))+(\W+|_|$)(?!\\)", Options)]
    private static partial Regex BracketedYearRegex { get; }

    // Last resort for titles that contain ( or [.
    [GeneratedRegex(@"^(?<title>.+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|\d+|\]|\W\d+)))+(\W+|_|$)(?!\\)", Options)]
    private static partial Regex BracketedTitleRegex { get; }

    [GeneratedRegex(@"^(?:(?:[-_\W](?<![)!]))*(?<year>(19|20)\d{2}(?!p|i|\d+|\W\d+)))+(\W+|_|$)(?<title>.+?)?$")]
    private static partial Regex YearFirstFolderRegex { get; }

    private static readonly Regex[] MovieTitleRegexes =
    [
        AnimeYearInParenthesesRegex, AnimeYearRegex, AnimeVersionedRegex, AnimeDoubleBracketsRegex, AnimeBracketsRegex,
        GermanRegex, EditionBeforeYearRegex, NormalRegex, PassThePopcornRegex, BracketedYearRegex, BracketedTitleRegex,
    ];

    private static readonly Regex[] MovieFolderRegexes = [.. MovieTitleRegexes, YearFirstFolderRegex];

    // md5 and other hashes, NZBGeek obfuscation, and junk names seen on indexers since 2014.
    [GeneratedRegex(@"^(?:[0-9a-zA-Z]{32}|[a-z0-9]{24}$|[A-Z]{11}\d{3}$|[a-z]{12}\d{3}$|Backup_\d{5,}S\d{2}-\d{2}$|123$|(?i:abc$|abc[-_. ]xyz|b00bs$))")]
    private static partial Regex HashedReleaseRegex { get; }

    [GeneratedRegex(@"(?:^|[-._ ])(p027|p0801)[-._ ]")]
    private static partial Regex ReversedTitleRegex { get; }

    [GeneratedRegex(@"[ ]+(?:AKA|\/)[ ]+", Options)]
    private static partial Regex AlternativeTitleRegex { get; }

    [GeneratedRegex(@"(.*) \([ ]*AKA[ ]+(.*)\)", Options)]
    private static partial Regex BracketedAlternativeTitleRegex { get; }

    [GeneratedRegex(@"[ ]+(?:A\.K\.A\.)[ ]+", Options)]
    private static partial Regex NormalizeAlternativeTitleRegex { get; }

    [GeneratedRegex(@"((?:\b|_)(?<!^|[^a-zA-Z0-9_']\w[^a-zA-Z0-9_'])([aà](?!$|[^a-zA-Z0-9_']\w[^a-zA-Z0-9_'])|an|the|and|or|of)(?!$)(?:\b|_))|\W|_", Options)]
    private static partial Regex NormalizeRegex { get; }

    [GeneratedRegex(@"(?<imdbid>tt\d{7,8})", Options)]
    private static partial Regex ImdbIdRegex { get; }

    [GeneratedRegex(@"tmdb(id)?-(?<tmdbid>\d+)", Options)]
    private static partial Regex TmdbIdRegex { get; }

    [GeneratedRegex(@"^(\d{1,10}|(tt)\d{1,10})$")]
    private static partial Regex NormalizableImdbIdRegex { get; }

    [GeneratedRegex(@"(?:(480|540|576|720|1080|2160)[ip]|[xh][\W_]?26[45]|DD\W?5\W1|[<>?*]|848x480|1280x720|1920x1080|3840x2160|4096x2160|(8|10)b(it)?|10-bit)\s*?(?![a-b0-9])", Options)]
    private static partial Regex SimpleTitleRegex { get; }

    [GeneratedRegex(@"\s*(?:[<>?*|])", Options)]
    private static partial Regex SimpleReleaseTitleRegex { get; }

    [GeneratedRegex(@"\[[a-z0-9 ._-]+\]$", Options)]
    private static partial Regex CleanQualityBracketsRegex { get; }

    [GeneratedRegex(@"(\&|\:|\\|\/)+")]
    private static partial Regex SpecialCharRegex { get; }

    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex PunctuationRegex { get; }

    [GeneratedRegex(@"^(a|an|the)\s", Options)]
    private static partial Regex ArticleWordRegex { get; }

    [GeneratedRegex(@"\b(part|special|edition|christmas)\b\s?", Options)]
    private static partial Regex SpecialEpisodeWordRegex { get; }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex DuplicateSpacesRegex { get; }

    [GeneratedRegex(@"^(?:\[.+?\])+")]
    private static partial Regex RequestInfoRegex { get; }

    [GeneratedRegex(@"\s")]
    private static partial Regex WhitespaceRegex { get; }

    [GeneratedRegex(@"[^a-z0-9\s-_]")]
    private static partial Regex InvalidSlugCharRegex { get; }

    public static ParsedMovieInfo? ParseMoviePath(string path)
    {
        string fileName = Path.GetFileName(path);
        string directoryName = Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty;

        return ParseMovieTitle(fileName, isDir: true)
            ?? ParseMovieTitle($"{directoryName} {fileName}")
            ?? ParseMovieTitle(directoryName + Path.GetExtension(path));
    }

    public static ParsedMovieInfo? ParseMovieTitle(string title, bool isDir = false)
    {
        string originalTitle = title;

        if (!ValidateBeforeParsing(title))
        {
            return null;
        }

        if (ReversedTitleRegex.IsMatch(title))
        {
            char[] reversed = RemoveFileExtension(title).ToCharArray();
            Array.Reverse(reversed);
            title = new string(reversed) + title[reversed.Length..];
        }

        string releaseTitle = RemoveFileExtension(title).Trim('-', '_').Replace('【', '[').Replace('】', ']');

        string simpleTitle = SimpleTitleRegex.Replace(releaseTitle, string.Empty);
        simpleTitle = WebsitePrefixRegex.Replace(simpleTitle, string.Empty);
        simpleTitle = WebsitePostfixRegex.Replace(simpleTitle, string.Empty);
        simpleTitle = CleanTorrentSuffixRegex.Replace(simpleTitle, string.Empty);
        simpleTitle = CleanQualityBracketsRegex.Replace(simpleTitle, m => QualityParser.ParseQualityName(m.Value).Quality != Quality.Unknown ? string.Empty : m.Value);

        foreach (Regex regex in isDir ? MovieFolderRegexes : MovieTitleRegexes)
        {
            Match match = regex.Match(simpleTitle);

            if (match.Success && ParseTitleMatch(match) is var (movieTitles, year, edition))
            {
                string simpleReleaseTitle = ReplaceTitleWithPlaceholder(SimpleReleaseTitleRegex.Replace(releaseTitle, string.Empty), match.Groups["title"]);
                Group subGroup = match.Groups["subgroup"];
                Group hash = match.Groups["hash"];

                return new ParsedMovieInfo
                {
                    MovieTitles = movieTitles,
                    Year = year,
                    Edition = edition ?? ParseEdition(simpleReleaseTitle),
                    ReleaseGroup = subGroup.Success && !string.IsNullOrWhiteSpace(subGroup.Value) ? subGroup.Value : ParseReleaseGroup(simpleReleaseTitle),
                    ReleaseHash = hash.Success && hash.Value.Trim('[', ']') is string hashValue && hashValue != "1280x720" ? hashValue : null,
                    HardcodedSubs = ParseHardcodedSubs(title),
                    Quality = QualityParser.ParseQuality(title),
                    OriginalTitle = originalTitle,
                    ReleaseTitle = releaseTitle,
                    SimpleReleaseTitle = simpleReleaseTitle,
                    ImdbId = ParseImdbId(simpleReleaseTitle),
                    TmdbId = ParseTmdbId(simpleReleaseTitle),
                };
            }
        }

        return null;
    }

    // Prefers false negatives: a title is only scene when it has dots, no spaces, a group and a quality.
    public static string? GetSceneTitle(string title)
    {
        if (!title.Contains('.', StringComparison.Ordinal) || title.Contains(' ', StringComparison.Ordinal))
        {
            return null;
        }

        ParsedMovieInfo? parsed = ParseMovieTitle(title);

        return parsed is null
            || parsed.ReleaseGroup is null
            || parsed.Quality.Quality == Quality.Unknown
            || string.IsNullOrWhiteSpace(parsed.PrimaryMovieTitle)
            || string.IsNullOrWhiteSpace(parsed.ReleaseTitle)
            ? null
            : parsed.ReleaseTitle;
    }

    public static bool IsSceneTitle(string title) => GetSceneTitle(title) is not null;

    public static string? ParseImdbId(string title)
    {
        Match match = ImdbIdRegex.Match(title);
        return match.Success ? match.Groups["imdbid"].Value : null;
    }

    public static int? ParseTmdbId(string title)
    {
        Match match = TmdbIdRegex.Match(title);
        return match.Success && int.TryParse(match.Groups["tmdbid"].Value, CultureInfo.InvariantCulture, out int tmdbId) ? tmdbId : null;
    }

    public static string? ParseEdition(string title)
    {
        Match match = ReportEditionRegex.Match(title);
        return match.Success && !string.IsNullOrWhiteSpace(match.Groups["edition"].Value) ? match.Groups["edition"].Value.Replace('.', ' ') : null;
    }

    public static string? ParseHardcodedSubs(string title)
    {
        Match? match = HardcodedSubsRegex.Matches(title).LastOrDefault();

        return match switch
        {
            null => null,
            _ when match.Groups["hcsub"].Success => match.Groups["hcsub"].Value,
            _ => "Generic Hardcoded Subs",
        };
    }

    public static string? NormalizeImdbId(string imdbId) =>
        NormalizableImdbIdRegex.IsMatch(imdbId) && imdbId.Length > 2
            ? $"tt{imdbId.Replace("tt", string.Empty, StringComparison.Ordinal).PadLeft(7, '0')}"
            : null;

    public static string ReplaceGermanUmlauts(string text) => text
        .Replace("ä", "ae", StringComparison.Ordinal)
        .Replace("ö", "oe", StringComparison.Ordinal)
        .Replace("ü", "ue", StringComparison.Ordinal)
        .Replace("Ä", "Ae", StringComparison.Ordinal)
        .Replace("Ö", "Oe", StringComparison.Ordinal)
        .Replace("Ü", "Ue", StringComparison.Ordinal)
        .Replace("ß", "ss", StringComparison.Ordinal);

    // Key used to match a parsed title against a known movie title.
    public static string CleanMovieTitle(string title) =>
        string.IsNullOrWhiteSpace(title) || long.TryParse(title, CultureInfo.InvariantCulture, out _)
            ? title
            : RemoveAccent(ReplaceGermanUmlauts(NormalizeRegex.Replace(title, string.Empty).ToLowerInvariant()));

    // Word tokens used to look for a language name in a release that is not part of the movie title.
    public static string NormalizeMovieTitle(string title)
    {
        title = SpecialEpisodeWordRegex.Replace(title, string.Empty);
        title = PunctuationRegex.Replace(title, " ");
        title = DuplicateSpacesRegex.Replace(title, " ");

        return title.Trim().ToLowerInvariant();
    }

    // Sort title: no punctuation, no leading article.
    public static string NormalizeTitle(string title)
    {
        title = PunctuationRegex.Replace(title, string.Empty);
        title = ArticleWordRegex.Replace(title, string.Empty);
        title = DuplicateSpacesRegex.Replace(title, " ");
        title = SpecialCharRegex.Replace(title, string.Empty);

        return title.Trim().ToLowerInvariant();
    }

    public static string SimplifyReleaseTitle(string title) => SimpleReleaseTitleRegex.Replace(title, string.Empty);

    public static string ToUrlSlug(string value, bool invalidDashReplacement = false, string? trimEndChars = "-_", string? deduplicateChars = "-_")
    {
        value = RemoveAccent(value.ToLowerInvariant());
        value = WhitespaceRegex.Replace(value, "-");
        value = InvalidSlugCharRegex.Replace(value, invalidDashReplacement ? "-" : string.Empty);

        if (!string.IsNullOrEmpty(trimEndChars))
        {
            value = value.Trim(trimEndChars.ToCharArray());
        }

        return string.IsNullOrEmpty(deduplicateChars) ? value : Regex.Replace(value, $"([{deduplicateChars}]){{2,}}", "$1");
    }

    private static (IReadOnlyList<string> MovieTitles, int? Year, string? Edition)? ParseTitleMatch(Match match)
    {
        Group titleGroup = match.Groups["title"];

        if (!titleGroup.Success || titleGroup.Value == "(")
        {
            return null;
        }

        string movieName = titleGroup.Value.Replace('_', ' ');
        movieName = NormalizeAlternativeTitleRegex.Replace(movieName, " AKA ");
        movieName = RequestInfoRegex.Replace(movieName, string.Empty).Trim(' ');
        movieName = JoinDottedParts(movieName.Split('.'));

        string unbracketedName = BracketedAlternativeTitleRegex.Replace(movieName, "$1 AKA $2");
        List<string> movieTitles = [movieName, .. AlternativeTitleRegex.Split(unbracketedName).Where(name => !string.IsNullOrWhiteSpace(name) && name != movieName)];

        int? year = int.TryParse(match.Groups["year"].Value, CultureInfo.InvariantCulture, out int parsedYear) ? parsedYear : null;
        string? edition = match.Groups["edition"].Success ? match.Groups["edition"].Value.Replace('.', ' ') : null;

        return (movieTitles, year, edition);
    }

    // Dots become spaces, except in acronyms (U.N.C.L.E., L.A.) and after Dr.
    private static string JoinDottedParts(string[] parts)
    {
        StringBuilder name = new();
        bool previousAcronym = false;

        for (int n = 0; n < parts.Length; n++)
        {
            string part = parts[n];
            string nextPart = n + 1 < parts.Length ? parts[n + 1] : string.Empty;
            bool isA = part.Equals("a", StringComparison.OrdinalIgnoreCase);
            bool nextIsDigit = nextPart.Length == 1 && char.IsAsciiDigit(nextPart[0]);

            if ((part.Length == 1 && !isA && !char.IsAsciiDigit(part[0]) && (previousAcronym || n < parts.Length - 1) && (previousAcronym || !nextIsDigit))
                || (isA && (previousAcronym || nextPart.Length == 1))
                || part.Equals("dr", StringComparison.OrdinalIgnoreCase))
            {
                name.Append(part).Append('.');
                previousAcronym = true;
            }
            else
            {
                if (previousAcronym)
                {
                    name.Append(' ');
                    previousAcronym = false;
                }

                name.Append(part).Append(' ');
            }
        }

        return name.ToString().Trim(' ');
    }

    // Hides the title so words in it (German, a group-like suffix) are not read as release info.
    private static string ReplaceTitleWithPlaceholder(string simpleReleaseTitle, Group titleGroup)
    {
        string placeholder = titleGroup.Value.Contains('.', StringComparison.Ordinal) ? "A.Movie" : "A Movie";

        // Indexes come from the cleaned title; a website prefix removed there can push them past the end.
        return string.IsNullOrWhiteSpace(titleGroup.Value) || titleGroup.Index + titleGroup.Length > simpleReleaseTitle.Length
            ? simpleReleaseTitle
            : simpleReleaseTitle.Remove(titleGroup.Index, titleGroup.Length).Insert(titleGroup.Index, placeholder);
    }

    private static bool ValidateBeforeParsing(string title) =>
        !(title.Contains("password", StringComparison.OrdinalIgnoreCase) && title.Contains("yenc", StringComparison.OrdinalIgnoreCase))
        && title.Any(char.IsLetterOrDigit)
        && !HashedReleaseRegex.IsMatch(RemoveFileExtension(title));

    private static string RemoveAccent(string text)
    {
        string decomposed = text.Normalize(NormalizationForm.FormD);
        return new string([.. decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)]).Normalize(NormalizationForm.FormC);
    }
}
