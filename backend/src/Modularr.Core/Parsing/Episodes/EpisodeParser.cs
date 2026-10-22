using System.Buffers;
using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Modularr.Core.Languages;
using Modularr.Core.Qualities;

namespace Modularr.Core.Parsing.Episodes;

// Port of Sonarr Parser (GPL-3.0): series release titles and file paths, without any series lookup.
public static partial class EpisodeParser
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"\.S\d+E00\.(?<episodetitle>.+?)(?:\.(?:720p|1080p|2160p|HDTV|WEB|WEBRip|WEB-DL)\.|$)", Options)]
    private static partial Regex SpecialEpisodeTitleRegex { get; }

    [GeneratedRegex(@"\.S\d+\.Special\.(?<episodetitle>.+?)(?:\.(?:720p|1080p|2160p|HDTV|WEB|WEBRip|WEB-DL)\.|$)", Options)]
    private static partial Regex SeasonSpecialTitleRegex { get; }

    [GeneratedRegex(@"^(?:Season[ ._-]*\d+|Specials)$")]
    private static partial Regex SeasonFolderNameRegex { get; }

    // A reversed release name ends with a reversed resolution or episode number.
    [GeneratedRegex(@"(?:^|[-._ ])(?:p027|p0801|\d{2,3}E-?\d{2}S)[-._ ]")]
    private static partial Regex ReversedTitleRegex { get; }

    [GeneratedRegex(@"\[[^\[\]]+\]|\([^()]+\)|\{[^{}]+\}")]
    private static partial Regex AnimeSeasonReleaseMetadataRegex { get; }

    [GeneratedRegex(@"\b(?:special|ova|ovd|ncop|nced)\b", Options)]
    private static partial Regex AnimeSpecialMarkerRegex { get; }


    [GeneratedRegex(@"(?<=[_.-])(?<airdate>(?<!\d)(?<airyear>[1-9]\d{1})(?<airmonth>[0-1][0-9])(?<airday>[0-3][0-9]))(?=[_.-])", Options)]
    private static partial Regex SixDigitAirDateRegex { get; }


    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+?[\(\[]?(?<year>\d{4})[\]\)]?", Options)]
    private static partial Regex YearInTitleRegex { get; }

    [GeneratedRegex(@"^(?:(?<title>.+?) \((?<title>.+?)\)|(?<title>.+?) \| (?<title>.+?)|(?<title>.+?) AKA (?<title>.+?)|(?<title>.+?) / (?<title>.+?))$", Options)]
    private static partial Regex TitleComponentsRegex { get; }

    [GeneratedRegex(@"\(\d+\)$")]
    private static partial Regex PartRegex { get; }



    [GeneratedRegex(@"^(?:S|Season|Saison|Series|Stagione|Sezon)[-_. ]*(?<season>(?<!\d+)\d{1,4}(?!\d+))(?:[_. ]+(?!\d+)|$)", Options)]
    private static partial Regex SeasonFolderRegex { get; }

    [GeneratedRegex(@"^[ex]?(?<episode>(?<!\d+)\d{1,3}(?!\d+))(?:[ex-](?<episode>(?<!\d+)\d{1,3}(?!\d+)))?(?:[_. ](?!\d+)(?<remaining>.+)|$)", Options)]
    private static partial Regex SimpleEpisodeNumberRegex { get; }


    // Season and episode number separated by a space (S02 01), only trusted in file names.
    [GeneratedRegex(@"(?<separator>^|[-_. ])S(?<season>(?<!\d+)\d{1,2}(?!\d+)) (?<episode>(?<!\d+)\d{1,2}(?!\d+))(?!\d+)", Options)]
    private static partial Regex SpaceSeparatedEpisodeRegex { get; }

    private static readonly SearchValues<char> Brackets = SearchValues.Create("[](){}");

    private static readonly string[] Numbers = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];

    private static readonly FrozenDictionary<string, int> ShortMonths = new Dictionary<string, int>
    {
        ["jan"] = 1, ["feb"] = 2, ["mar"] = 3, ["apr"] = 4, ["may"] = 5, ["jun"] = 6,
        ["jul"] = 7, ["aug"] = 8, ["sep"] = 9, ["oct"] = 10, ["nov"] = 11, ["dec"] = 12,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static ParsedEpisodeInfo? ParsePath(string path)
    {
        string fileName = SpaceSeparatedEpisodeRegex.Replace(Path.GetFileName(path), "${separator}S${season}E${episode}");
        string directory = Path.GetFileName(Path.GetDirectoryName(path)) ?? "";
        ParsedEpisodeInfo? result = ParseTitle(fileName);

        // Parse the folder and the file separately, combine them when both parse.
        Match episodeNumber = SimpleEpisodeNumberRegex.Match(fileName);
        if (episodeNumber.Success && (result is null || result.IsMiniSeries || result.AbsoluteEpisodeNumbers.Count > 0))
        {
            Match season = SeasonFolderRegex.Match(directory);
            if (season.Success)
            {
                CaptureCollection episodes = episodeNumber.Groups["episode"].Captures;
                int first = ParseNumber(episodes[0].Value);
                int last = ParseNumber(episodes[^1].Value);
                string pathTitle = string.Create(CultureInfo.InvariantCulture, $"S{season.Groups["season"].Value}E{first:00}");

                if (first != last)
                {
                    pathTitle += string.Create(CultureInfo.InvariantCulture, $"-E{last:00}");
                }

                if (episodeNumber.Groups["remaining"].Success)
                {
                    pathTitle += $" {episodeNumber.Groups["remaining"].Value}";
                }

                return ParseTitle(pathTitle);
            }
        }

        if (directory.Length == 0)
        {
            return result;
        }

        if (result is null && int.TryParse(Path.GetFileNameWithoutExtension(fileName), CultureInfo.InvariantCulture, out int number))
        {
            result = ParseTitle(directory);

            if (result is not null && result.AbsoluteEpisodeNumbers.Contains(number))
            {
                result = result with { AbsoluteEpisodeNumbers = [number] };
            }
            else if (result is not null && result.EpisodeNumbers.Contains(number))
            {
                result = result with { EpisodeNumbers = [number] };
            }
            else
            {
                result = null;
            }
        }

        return result ?? ParseTitle($"{directory} {fileName}") ?? ParseTitle(directory + Path.GetExtension(path));
    }

    public static ParsedEpisodeInfo? ParseTitle(string title)
    {
        if (!ValidateBeforeParsing(title))
        {
            return null;
        }

        title = UnreverseTitle(title);

        string releaseTitle = ReleaseTitle.RemoveFileExtension(title).Replace('【', '[').Replace('】', ']');
        releaseTitle = ReleaseTitle.PreSubstitute(releaseTitle);

        string simpleTitle = ReleaseTitle.SimplifyTitle(releaseTitle);

        Match sixDigitAirDate = SixDigitAirDateRegex.Match(simpleTitle);
        if (sixDigitAirDate.Success && (sixDigitAirDate.Groups["airmonth"].Value != "00" || sixDigitAirDate.Groups["airday"].Value != "00"))
        {
            string fixedDate = $"20{sixDigitAirDate.Groups["airyear"].Value}.{sixDigitAirDate.Groups["airmonth"].Value}.{sixDigitAirDate.Groups["airday"].Value}";
            simpleTitle = simpleTitle.Replace(sixDigitAirDate.Groups["airdate"].Value, fixedDate, StringComparison.Ordinal);
        }

        foreach (Regex regex in ReportTitleRegexes)
        {
            MatchCollection matches = regex.Matches(simpleTitle);
            if (matches.Count == 0)
            {
                continue;
            }

            ParsedEpisodeInfo? result = ParseMatchCollection(matches, releaseTitle, out bool invalidDate);
            if (invalidDate)
            {
                return null;
            }

            if (result is null)
            {
                continue;
            }

            if (result.FullSeason && result.ReleaseTokens.Contains("Special", StringComparison.OrdinalIgnoreCase))
            {
                result = result with { FullSeason = false, Special = true };
            }

            Group subGroup = matches[0].Groups["subgroup"];

            return result with
            {
                Languages = LanguageParser.ParseLanguages(result.ReleaseTokens),
                Quality = QualityParser.ParseQuality(title),
                ReleaseGroup = subGroup.Success && !string.IsNullOrWhiteSpace(subGroup.Value) ? subGroup.Value : ReleaseGroupParser.ParseReleaseGroup(releaseTitle),
                ReleaseHash = ReleaseTitle.GetReleaseHash(matches[0]),
            };
        }

        return null;
    }

    // Anime season packs are often named after the season's own title, with no season or episode number.
    public static ParsedEpisodeInfo? ParseSeasonTitle(string title)
    {
        string releaseTitle = ReleaseTitle.RemoveFileExtension(title);

        if (AnimeSpecialMarkerRegex.IsMatch(releaseTitle))
        {
            return null;
        }

        string seriesTitle = ReleaseTitle.DuplicateSpacesRegex.Replace(AnimeSeasonReleaseMetadataRegex.Replace(releaseTitle, " "), " ").Trim();

        // Brackets left over mean the metadata could not be stripped cleanly.
        if (seriesTitle.Length == 0 || seriesTitle.AsSpan().ContainsAny(Brackets))
        {
            return null;
        }

        return new ParsedEpisodeInfo
        {
            ReleaseTitle = releaseTitle,
            SeriesTitle = seriesTitle,
            SeriesTitleInfo = new SeriesTitleInfo(seriesTitle, seriesTitle, 0, []),
            FullSeason = true,
            IsSeasonTitle = true,
            Languages = LanguageParser.ParseLanguages(releaseTitle),
            Quality = QualityParser.ParseQuality(title),
            ReleaseGroup = ReleaseGroupParser.ParseReleaseGroup(releaseTitle),
        };
    }

    public static string ParseSeriesName(string title) => ParseTitle(title)?.SeriesTitle ?? ReleaseTitle.CleanTitle(title);

    public static string NormalizeEpisodeTitle(string title)
    {
        Match special = SpecialEpisodeTitleRegex.Match(title);
        if (!special.Success)
        {
            special = SeasonSpecialTitleRegex.Match(title);
        }

        if (special.Success)
        {
            title = special.Groups["episodetitle"].Value;
        }

        title = ReleaseTitle.PunctuationRegex.Replace(PartRegex.Replace(title, ""), " ");

        return ReleaseTitle.DuplicateSpacesRegex.Replace(title, " ").Trim().ToLowerInvariant();
    }

    private static bool ValidateBeforeParsing(string title)
    {
        if ((title.Contains("password", StringComparison.OrdinalIgnoreCase) && title.Contains("yenc", StringComparison.OrdinalIgnoreCase))
            || !title.Any(char.IsLetterOrDigit))
        {
            return false;
        }

        string titleWithoutExtension = ReleaseTitle.RemoveFileExtension(title);

        return !ReleaseTitle.IsHashed(titleWithoutExtension) && !SeasonFolderNameRegex.IsMatch(titleWithoutExtension);
    }

    private static string UnreverseTitle(string title)
    {
        if (!ReversedTitleRegex.IsMatch(title))
        {
            return title;
        }

        char[] reversed = ReleaseTitle.RemoveFileExtension(title).ToCharArray();
        Array.Reverse(reversed);

        return string.Concat(new string(reversed), title.AsSpan(reversed.Length));
    }

    private static ParsedEpisodeInfo? ParseMatchCollection(MatchCollection matches, string releaseTitle, out bool invalidDate)
    {
        invalidDate = false;
        Match first = matches[0];
        string seriesName = ReleaseTitle.RequestInfoRegex.Replace(first.Groups["title"].Value.Replace('.', ' ').Replace('_', ' '), "").Trim(' ');
        int lastIndex = EndIndex(first.Groups["title"]);
        _ = int.TryParse(first.Groups["airyear"].Value, CultureInfo.InvariantCulture, out int airYear);

        ParsedEpisodeInfo result;

        if (airYear < 1900)
        {
            result = new ParsedEpisodeInfo { ReleaseTitle = releaseTitle, SeasonNumbers = [0] };

            foreach (Match match in matches)
            {
                CaptureCollection episodes = match.Groups["episode"].Captures;
                CaptureCollection absolutes = match.Groups["absoluteepisode"].Captures;

                // No episode at all is fine: that is a full season.
                if (episodes.Count > 0)
                {
                    int firstEpisode = ParseNumber(episodes[0].Value);
                    int lastEpisode = ParseNumber(episodes[^1].Value);

                    if (firstEpisode > lastEpisode)
                    {
                        return null;
                    }

                    result = result with
                    {
                        EpisodeNumbers = [.. Enumerable.Range(firstEpisode, lastEpisode - firstEpisode + 1)],
                        Special = result.Special || match.Groups["special"].Success,
                        IsSplitEpisode = result.IsSplitEpisode || match.Groups["splitepisode"].Success,
                    };
                    lastIndex = Math.Max(lastIndex, EndIndex(episodes[^1]));
                }

                if (absolutes.Count > 0)
                {
                    decimal firstAbsolute = ParseDecimal(absolutes[0].Value);
                    decimal lastAbsolute = ParseDecimal(absolutes[^1].Value);

                    if (firstAbsolute > lastAbsolute)
                    {
                        return null;
                    }

                    if (firstAbsolute % 1 != 0 || lastAbsolute % 1 != 0)
                    {
                        // A decimal number is a recap special, never part of a range.
                        if (absolutes.Count != 1)
                        {
                            return null;
                        }

                        result = result with { SpecialAbsoluteEpisodeNumbers = [firstAbsolute], Special = true };
                        lastIndex = Math.Max(lastIndex, EndIndex(absolutes[0]));
                    }
                    else
                    {
                        result = result with
                        {
                            AbsoluteEpisodeNumbers = [.. Enumerable.Range((int)firstAbsolute, (int)(lastAbsolute - firstAbsolute) + 1)],
                            Special = result.Special || match.Groups["special"].Success,
                        };
                        lastIndex = Math.Max(lastIndex, EndIndex(absolutes[^1]));
                    }
                }

                if (episodes.Count == 0 && absolutes.Count == 0)
                {
                    // Extras and subpacks are flagged so they can be filtered out.
                    if (!string.IsNullOrWhiteSpace(first.Groups["extras"].Value))
                    {
                        result = result with { IsSeasonExtra = true };
                    }

                    string seasonPart = first.Groups["seasonpart"].Value;

                    result = !string.IsNullOrWhiteSpace(seasonPart) ? result with { SeasonPart = int.Parse(seasonPart, CultureInfo.InvariantCulture), IsPartialSeason = true }
                        : first.Groups["special"].Success ? result with { Special = true }
                        : result with { FullSeason = true };
                }

                // "S1E1-6 of 6" is the whole season.
                if (episodes.Count == 2 && first.Groups["episodecount"].Success && episodes[^1].Value == first.Groups["episodecount"].Value)
                {
                    result = result with { EpisodeNumbers = [], FullSeason = true };
                }
            }

            List<int> seasons = [];
            foreach (Capture season in first.Groups["season"].Captures)
            {
                if (int.TryParse(season.Value, CultureInfo.InvariantCulture, out int parsedSeason))
                {
                    seasons.Add(parsedSeason);
                    lastIndex = Math.Max(lastIndex, EndIndex(season));
                }
            }

            List<int> distinctSeasons = [.. seasons.Distinct().Order()];

            if (distinctSeasons.Count is 1 or > 2)
            {
                result = result with { SeasonNumbers = distinctSeasons };
            }
            else if (distinctSeasons.Count == 2)
            {
                result = result with { SeasonNumbers = [.. Enumerable.Range(distinctSeasons[0], distinctSeasons[1] - distinctSeasons[0] + 1)] };
            }
            else if (result.AbsoluteEpisodeNumbers.Count == 0 && result.EpisodeNumbers.Count > 0)
            {
                // No season and not absolute: a mini series, season 1.
                result = result with { SeasonNumbers = [1], IsMiniSeries = true };
            }
        }
        else
        {
            DateOnly? airDate = ParseAirDate(first, airYear);
            if (airDate is null)
            {
                invalidDate = true;
                return null;
            }

            lastIndex = Math.Max(lastIndex, EndIndex(first.Groups["airyear"]));
            lastIndex = Math.Max(lastIndex, EndIndex(first.Groups["airmonth"]));
            lastIndex = Math.Max(lastIndex, EndIndex(first.Groups["airday"]));

            Group part = first.Groups["part"];
            result = new ParsedEpisodeInfo
            {
                ReleaseTitle = releaseTitle,
                SeasonNumbers = [0],
                AirDate = airDate,
                DailyPart = part.Success ? int.Parse(part.Value, CultureInfo.InvariantCulture) : null,
            };
        }

        return result with
        {
            ReleaseTokens = lastIndex < releaseTitle.Length ? releaseTitle[lastIndex..] : releaseTitle,
            SeriesTitle = seriesName,
            SeriesTitleInfo = GetSeriesTitleInfo(seriesName, first),
        };
    }

    private static DateOnly? ParseAirDate(Match match, int airYear)
    {
        int airMonth;
        int airDay;

        if (match.Groups["ambiguousairmonth"].Success && match.Groups["ambiguousairday"].Success)
        {
            airMonth = int.Parse(match.Groups["ambiguousairmonth"].Value, CultureInfo.InvariantCulture);
            airDay = int.Parse(match.Groups["ambiguousairday"].Value, CultureInfo.InvariantCulture);

            // 05-06-2024 could be May 6th or June 5th.
            if (airDay <= 12 && airMonth <= 12)
            {
                return null;
            }
        }
        else if (match.Groups["shortairmonth"].Success)
        {
            airMonth = ShortMonths[match.Groups["shortairmonth"].Value];
            airDay = int.Parse(match.Groups["airday"].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            airMonth = int.Parse(match.Groups["airmonth"].Value, CultureInfo.InvariantCulture);
            airDay = int.Parse(match.Groups["airday"].Value, CultureInfo.InvariantCulture);
        }

        // Scene fail: day and month swapped.
        if (airMonth > 12)
        {
            (airDay, airMonth) = (airMonth, airDay);
        }

        if (airMonth is < 1 or > 12 || airDay < 1 || airDay > DateTime.DaysInMonth(airYear, airMonth))
        {
            return null;
        }

        DateOnly airDate = new(airYear, airMonth, airDay);

        // Early releases are fine, a date far in the future is a parse error.
        if (airDate > DateOnly.FromDateTime(DateTime.Now.AddDays(14)))
        {
            return null;
        }

        // Before 1970 only the Plex DVR format (title with its year) is trusted.
        return airDate < new DateOnly(1970, 1, 1) && string.IsNullOrWhiteSpace(match.Groups["titleyear"].Value) ? null : airDate;
    }

    private static SeriesTitleInfo GetSeriesTitleInfo(string title, Match match)
    {
        Match year = YearInTitleRegex.Match(title);
        string titleWithoutYear = year.Success ? year.Groups["title"].Value : title;
        Match components = TitleComponentsRegex.Match(titleWithoutYear);
        CaptureCollection matchedTitles = match.Groups["title"].Captures;

        IReadOnlyList<string> allTitles = components.Success ? [.. components.Groups["title"].Captures.Select(c => c.Value)]
            : matchedTitles.Count > 1 ? [.. matchedTitles.Select(c => c.Value.Replace('.', ' ').Replace('_', ' '))]
            : [];

        return new SeriesTitleInfo(title, titleWithoutYear, year.Success ? int.Parse(year.Groups["year"].Value, CultureInfo.InvariantCulture) : 0, allTitles);
    }

    private static int EndIndex(Capture capture) => capture.Index + capture.Length;

    // Episode numbers can be any Unicode digits (fullwidth, Arabic-Indic) or a spelled out One to Nine.
    private static int ParseNumber(string value)
    {
        string normalized = ConvertToNumerals(value);
        return int.TryParse(normalized, CultureInfo.InvariantCulture, out int number)
            ? number
            : Array.IndexOf(Numbers, value.ToLowerInvariant());
    }

    private static decimal ParseDecimal(string value) =>
        decimal.Parse(ConvertToNumerals(value), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string ConvertToNumerals(string input)
    {
        StringBuilder result = new(input.Length);
        foreach (char c in input.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsNumber(c))
            {
                result.Append((int)char.GetNumericValue(c));
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }
}
