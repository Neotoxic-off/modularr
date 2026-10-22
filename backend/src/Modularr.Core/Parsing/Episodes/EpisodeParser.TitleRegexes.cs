using System.Text.RegularExpressions;

namespace Modularr.Core.Parsing.Episodes;

// Generated from Sonarr Parser.ReportTitleRegex (GPL-3.0); order matters, the first regex that parses wins.
public static partial class EpisodeParser
{
    private static readonly Regex[] ReportTitleRegexes =
    [
        DailyEpisodeYearSeriesTitleAirRegex,
        DailyEpisodesWithoutTitleRegex,
        MultiPartEpisodesWithoutTitleRegex,
        MultiPartEpisodesWithoutTitle2Regex,
        EpisodesWithoutTitleMultiRegex,
        SplitEpisodesRegex,
        EpisodesWithoutTitleSingleRegex,
        AnimeSubgroupTitleAbsoluteRegex,
        AnimeSubgroupTitleAbsoluteSeasonEpisodeRegex,
        AnimeSubgroupTitleSeasonEpisodeRegex,
        AnimeSubgroupTitleEpisodeAbsoluteEpisodeRegex,
        AnimeSubgroupTitleAbsoluteEpisodeNumberRegex,
        AnimeSubgroupTitleMultiEpisodeOptionalRegex,
        AnimeSubgroupTitleSingleEpisodeAbsoluteRegex,
        AnimeSubgroupTitleTrailingNumberAbsoluteRegex,
        AnimeSubgroupTitleSeasonNumberBracketsRegex,
        AnimeSubgroupTitleTrailing3DigitRegex,
        AnimeSubgroupMultiSeasonPackRegex,
        AnimeSubgroupPartialSeasonPackRegex,
        AnimeSubgroupTitleTrailingNumberAbsolute2Regex,
        AnimeSubgroupTitleTrailingNumberSRegex,
        AnimeSubgroupTitleTrailingNumberAbsolute3Regex,
        AnimeSubgroupTitleAbsoluteEpisodeNumber2Regex,
        AnimeSubgroupTitleAbsoluteEpisodeNumber3Regex,
        AnimeSubgroupTitleRegex,
        AnimeSubgroupTitleAbsoluteEpisodeNumber4Regex,
        MultiEpisodeRepeatedRegex,
        MultiEpisodeRepeated2Regex,
        SingleEpisodesTitleFollowed5SpRegex,
        SingleEpisodesTitleTrailingInfoSlashesRegex,
        AnimeTitleSeasonEpisodenumberAbsoluteEpisodeRegex,
        MultiEpisodeTitleTrailingInfoSlashesRegex,
        AnimeTitleAbsoluteEpisodeNumberSubgroupRegex,
        AnimeTitleAbsoluteEpisodeNumberSubgroup2Regex,
        AnimeTitleAbsoluteEpisodeNumberSubgroup3Regex,
        AnimeTitleTrailingNumberAbsoluteEpisodeRegex,
        AnimeTitleAbsoluteEpisodeNumberHashRegex,
        EpisodesAirdateSeasonEpisodeNumberCaptureRegex,
        EpisodesAirdateSeasonEpisodeNumberRegex,
        EpisodesAbsoluteEpisodeNumberAirdateRegex,
        SingleMultiEpisodeReleasesMultipleTitlesRegex,
        MultiEpisodeTitleRegex,
        MultiEpisodeTitle2Regex,
        EpisodesChineseTitleEnglishTitleSingleRegex,
        EpisodesTitleSingleEpisodesMultiEpisodeRegex,
        EpisodesTitle4DigitSeasonNumberRegex,
        EpisodesTitle4DigitSeasonNumber2Regex,
        EpisodesTitleSingleEpisodesRegex,
        MultiSeasonPackRegex,
        PartialSeasonPackRegex,
        Anime4DigitAbsoluteEpisodeNumberRegex,
        Anime4DigitAbsoluteEpisodeNumber2Regex,
        MiniSeriesTreatedAsSeason1Regex,
        N5DigitEpisodeNumberTitleRegex,
        N5DigitMultiEpisodeTitleRegex,
        EpisodesAirdatePartRegex,
        EpisodesAirdateRegex,
        AnimeTitle4DigitAbsoluteEpisodeRegex,
        MiniSeriesYearTitleTreatedAsRegex,
        N3DigitSeasonNumberRegex,
        MiniSeriesTreatedAsSeason12Regex,
        SupportsSeason01Episode03Regex,
        MultiEpisodeEpisodesSquareBracketsRegex,
        MultiEpisodeEpisodesBracketsSeriesTitleRegex,
        MultiEpisodeReleaseNoSpaceBetweenRegex,
        MultiEpisodeSingleEpisodeNumbersRegex,
        SingleMultiEpisodeReleasesMultipleTitles2Regex,
        MultiEpisodeEpisodesBracketsSeriesTitle2Regex,
        SingleEpisodeSeasonEpisodeS1e1S1Regex,
        N3DigitSeasonS010e05Regex,
        SeparatedSeasonEpisodeNumbersS01E01Regex,
        SeasonEpisodeNumbersSquareBracketsRegex,
        AnimeTitleSeasonNumberAbsoluteEpisodeRegex,
        AnimeFrenchTitlesSingleEpisodeNumbersRegex,
        AnimeAbsoluteEpisodeNumberSquareBracketsRegex,
        JapaneseVarietyShowsLeadingDateRegex,
        SeasonOnlyReleasesFollowedYearRegex,
        SeasonOnlyReleasesRegex,
        N4DigitSeasonOnlyReleasesRegex,
        SpanishTrackerReleasesRegex,
        Supports103113NamingRegex,
        N4DigitEpisodeNumberRegex,
        N4DigitEpisodeNumber2Regex,
        TurkishTrackerReleasesRegex,
        EpisodesAirdate2Regex,
        EpisodesAirdate3Regex,
        EpisodesAirdate4Regex,
        AnimeTitleAbsoluteEpisodeNumberRegex,
        DailyEpisodesThatUseShortMonthRegex,
        Supports11031113NamingRegex,
        DutchFlemishReleaseTitlesRegex,
        EpisodesSingleDigitEpisodeNumberRegex,
        ItunesSeason105TitleExtRegex,
        Itunes105TitleExtRegex,
        MiniSeriesTreatedAsSeason13Regex,
        MiniSeriesTreatedAsSeason14Regex,
        AnimeRangeTitleAbsoluteEpisodeNumberRegex,
        AnimeTitleAbsoluteEpisodeNumber2Regex,
        AnimeTitleEpisodeAbsoluteEpisodeNumberRegex,
        AnimeRangeTitleAbsoluteEpisodeNumber2Regex,
        AnimeTitleEpisodeEpisodioAbsoluteEpisodeRegex,
        AnimeTitleAbsoluteEpisodeNumberFromRegex,
        SimpleSeasonReleasesFormatSeriesTemporadaRegex,
        AnimeTitleAbsoluteEpisodeNumber3Regex,
        AnimeTitleAbsoluteEpisodeNumber4Regex,
        ExtantTerribleMultiEpisodeNamingRegex,
        EpisodesWithoutTitleSingleEpisodeNumbersRegex,
        AnimeOvaSpecialRegex,
        EpisodesTitle3DigitSeasonNumberRegex,
    ];

    // Daily episode with year in series title and air time after date (Plex DVR format)
    [GeneratedRegex(@"^^(?<title>.+?\((?<titleyear>\d{4})\))[-_. ]+(?<airyear>19[4-9]\d|20\d\d)(?<sep>[-_]?)(?<airmonth>0\d|1[0-2])\k<sep>(?<airday>[0-2]\d|3[01])[-_. ]\d{2}[-_. ]\d{2}[-_. ]\d{2}", Options)]
    private static partial Regex DailyEpisodeYearSeriesTitleAirRegex { get; }

    // Daily episodes without title (2018-10-12, 20181012) (Strict pattern to avoid false matches)
    [GeneratedRegex(@"^(?<airyear>19[6-9]\d|20\d\d)(?<sep>[-_]?)(?<airmonth>0\d|1[0-2])\k<sep>(?<airday>[0-2]\d|3[01])(?!\d)", Options)]
    private static partial Regex DailyEpisodesWithoutTitleRegex { get; }

    // Multi-Part episodes without a title (S01E05.S01E06)
    [GeneratedRegex(@"^(?:\W*S(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:e{1,2}(?<episode>\d{1,3}(?!\d+)))+){2,}", Options)]
    private static partial Regex MultiPartEpisodesWithoutTitleRegex { get; }

    // Multi-Part episodes without a title (1x05.1x06)
    [GeneratedRegex(@"^(?:\W*(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:x{1,2}(?<episode>\d{1,3}(?!\d+)))+){2,}", Options)]
    private static partial Regex MultiPartEpisodesWithoutTitle2Regex { get; }

    // Episodes without a title, Multi (S01E04E05, 1x04x05, etc)
    [GeneratedRegex(@"^(?:S?(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:[-_]|[ex]){1,2}(?<episode>\d{2,3}(?!\d+))){2,})", Options)]
    private static partial Regex EpisodesWithoutTitleMultiRegex { get; }

    // Split episodes (S01E05a, S01E05b, etc)
    [GeneratedRegex(@"^(?<title>.+?)(?:S?(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:[-_ ]?[ex])(?<episode>\d{2,3}(?!\d+))(?<splitepisode>[a-d])(?:[ _.])))", Options)]
    private static partial Regex SplitEpisodesRegex { get; }

    // Episodes without a title, Single (S01E05, 1x05)
    [GeneratedRegex(@"^(?:S?(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:[-_ ]?[ex])(?<episode>\d{2,3}(?!\d+))))", Options)]
    private static partial Regex EpisodesWithoutTitleSingleRegex { get; }

    // Anime - [SubGroup] Title Absolute (Season+Episode)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+))(?:[-_. ])+\((?:S(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+))))(?:v\d+)?(?:\)(?!\d+)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeSubgroupTitleAbsoluteRegex { get; }

    // Anime - [SubGroup] Title Absolute - Season+Episode
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+))(?:[-_. ](?<![()\[!]))+(?:S(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+))))(?:v\d+)?(?:[_. ](?!\d+)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])$", Options)]
    private static partial Regex AnimeSubgroupTitleAbsoluteSeasonEpisodeRegex { get; }

    // Anime - [SubGroup] Title Season+Episode
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)(?:[-_\W](?<![()\[!]))+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+)))+)(?:v\d+)?(?:[_. ](?!\d+)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeSubgroupTitleSeasonEpisodeRegex { get; }

    // Anime - [SubGroup] Title Episode Absolute Episode Number ([SubGroup] Series Title Episode 01)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>.+?)[-_. ]+?(?:Episode)(?:[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+)))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeSubgroupTitleEpisodeAbsoluteEpisodeRegex { get; }

    // Anime - [SubGroup] Title Absolute Episode Number + Season+Episode
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+(?<absoluteepisode>\d{2,3}(\.\d{1,2})?))+(?:_|-|\s|\.)+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+)))+).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.)", Options)]
    private static partial Regex AnimeSubgroupTitleAbsoluteEpisodeNumberRegex { get; }

    // Anime - [SubGroup] Title Multi-episode + optional Absolute Episode Number
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)(?:[-_\W](?<![()\[!]))+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+))){2,})(?:(?:_|-|\s|\.)+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+|\-[a-z])))*.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeSubgroupTitleMultiEpisodeOptionalRegex { get; }

    // Anime - [SubGroup] Title Single episode + Absolute Episode Number
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)(?:[-_\W](?<![()\[!]))+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+))))(?:(?:_|-|\s|\.)+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+|\-[a-z])))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeSubgroupTitleSingleEpisodeAbsoluteRegex { get; }

    // Anime - [SubGroup] Title with trailing number Absolute Episode Number - Batch separated with tilde
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?[^-]+?)(?:(?<![-_. ]|\b[0]\d+) - )[-_. ]?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+))\s?~\s?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+))(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", Options)]
    private static partial Regex AnimeSubgroupTitleTrailingNumberAbsoluteRegex { get; }

    // Anime - [SubGroup] Title with season number in brackets Absolute Episode Number
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>[^-]+?)[_. ]+?\(Season[_. ](?<season>\d+)\)[-_. ]+?(?:[-_. ]?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+)))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", Options)]
    private static partial Regex AnimeSubgroupTitleSeasonNumberBracketsRegex { get; }

    // Anime - [SubGroup] Title with trailing 3-digit number and sub title - Absolute Episode Number
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+?)\][-_. ]?(?<title>[^]]+?)(?:[-_. ]{3}?(?<absoluteepisode>\d{2}(\.\d{1,2})?(?!-?\d+|-[a-z]+)))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", Options)]
    private static partial Regex AnimeSubgroupTitleTrailing3DigitRegex { get; }

    // Anime - [SubGroup] Multi-season pack
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)[-_. ]+S(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:(?:-S?|[-_. ]{1,3}S)(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))){1,}", Options)]
    private static partial Regex AnimeSubgroupMultiSeasonPackRegex { get; }

    // Anime - [SubGroup] Partial season pack
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)(?:\W+S(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))\W+(?:(?:(?:Part|Vol)\W?|(?<!\d+\W+)e|p)(?<seasonpart>\d{1,2}(?!\d+)))+)", Options)]
    private static partial Regex AnimeSubgroupPartialSeasonPackRegex { get; }

    // Anime - [SubGroup] Title with trailing number Absolute Episode Number
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>[^-]+?)(?:(?<![-_. ]|\b[0]\d+) - )(?:[-_. ]?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+)))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", Options)]
    private static partial Regex AnimeSubgroupTitleTrailingNumberAbsolute2Regex { get; }

    // Anime - [SubGroup] Title with trailing number S## (Full season)
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)[-_. ]+(?:S(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?![ex]?\d+))).+?(?:$|\.mkv)", Options)]
    private static partial Regex AnimeSubgroupTitleTrailingNumberSRegex { get; }

    // Anime - [SubGroup] Title with trailing number Absolute Episode Number
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>[^-]+?)(?:(?<![-_. ]|\b[0]\d+)[_ ]+)(?:[-_. ]?(?<absoluteepisode>\d{3}(\.\d{1,2})?(?!\d+|-[a-z]+)))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", Options)]
    private static partial Regex AnimeSubgroupTitleTrailingNumberAbsolute3Regex { get; }

    // Anime - [SubGroup] Title - Absolute Episode Number
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)(?:(?<!\b[0]\d+))(?:[. ]-[. ](?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+|[-])))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", Options)]
    private static partial Regex AnimeSubgroupTitleAbsoluteEpisodeNumber2Regex { get; }

    // Anime - [SubGroup] Title Absolute Episode Number - Absolute Episode Number (batches without full separator between title and absolute episode numbers)
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)(?:(?<!\b[0]\d+))(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+|[-]))[. ]-[. ](?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+|[-]))(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", Options)]
    private static partial Regex AnimeSubgroupTitleAbsoluteEpisodeNumber3Regex { get; }

    // Anime - [SubGroup] Title (Season only)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ])?(?<title>.+?)[-_. ]+?[\[(](?:S|Season|Saison|Series|Stagione|Sezon)[-_. ]?(?<season>\d{1,2}(?![-_. ]?\d+))(?:[-_. )\]]|$)+(?<extras>EXTRAS|SUBPACK)?(?!\\)", Options)]
    private static partial Regex AnimeSubgroupTitleRegex { get; }

    // Anime - [SubGroup] Title Absolute Episode Number
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)[-_. ]+\(?(?:[-_. ]?#?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+|-[a-z]+)))+\)?(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", Options)]
    private static partial Regex AnimeSubgroupTitleAbsoluteEpisodeNumber4Regex { get; }

    // Multi-episode Repeated (S01E05 - S01E06)
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:e|[-_. ]e){1,2}(?<episode>\d{1,3}(?!\d+)))+){2,}", Options)]
    private static partial Regex MultiEpisodeRepeatedRegex { get; }

    // Multi-episode Repeated (1x05 - 1x06)
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:x{1,2}(?<episode>\d{1,3}(?!\d+)))+){2,}", Options)]
    private static partial Regex MultiEpisodeRepeated2Regex { get; }

    // Single episodes with a title (S01E05, 1x05, etc) followed by ".5 [SP]"
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+|(?:[ex]|\W[ex]|_|-){1,2}\d+))(?<special>\.5[ .]\[SP\]))", Options)]
    private static partial Regex SingleEpisodesTitleFollowed5SpRegex { get; }

    // Single episodes with a title (S01E05, 1x05, etc) and trailing info in slashes
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+|(?:[ex]|\W[ex]|_|-){1,2}\d+))).+?(?:\[.+?\])(?!\\)", Options)]
    private static partial Regex SingleEpisodesTitleTrailingInfoSlashesRegex { get; }

    // Anime - Title Season EpisodeNumber + Absolute Episode Number [SubGroup]
    [GeneratedRegex(@"^(?<title>.+?)(?:[-_\W](?<![()\[!]))+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:[ex]|\W[ex]|-){1,2}(?<episode>(?<!\d+)\d{2}(?!\d+)))+)[-_. (]+?(?:[-_. ]?(?<absoluteepisode>(?<!\d+)\d{3}(\.\d{1,2})?(?!\d+|[pi])))+.+?\[(?<subgroup>.+?)\](?:$|\.mkv)", Options)]
    private static partial Regex AnimeTitleSeasonEpisodenumberAbsoluteEpisodeRegex { get; }

    // Multi-Episode with a title (S01E05E06, S01E05-06, S01E05 E06, etc) and trailing info in slashes
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+)))+).+?(?:\[.+?\])(?!\\)", Options)]
    private static partial Regex MultiEpisodeTitleTrailingInfoSlashesRegex { get; }

    // Anime - Title Absolute Episode Number [SubGroup] [Hash]? (Series Title Episode 99-100 [RlsGroup] [ABCD1234])
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]Episode(?:[-_. ]+(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+)))+(?:.+?)\[(?<subgroup>.+?)\].*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeTitleAbsoluteEpisodeNumberSubgroupRegex { get; }

    // Anime - Title Absolute Episode Number [SubGroup] [Hash]
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:_|-|\s|\.)+(?<absoluteepisode>\d{3}(\.\d{1,2})(?!\d+)))+(?:.+?)\[(?<subgroup>.+?)\].*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeTitleAbsoluteEpisodeNumberSubgroup2Regex { get; }

    // Anime - Title Absolute Episode Number (Year) [SubGroup]
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2}(?!\d+))[-_. ](\(\d{4}\))[-_. ]\[(?<subgroup>.+?)\]", Options)]
    private static partial Regex AnimeTitleAbsoluteEpisodeNumberSubgroup3Regex { get; }

    // Anime - Title with trailing number, Absolute Episode Number and hash
    [GeneratedRegex(@"^(?<title>[^-]+?)(?:(?<![-_. ]|\b[0]\d+) - )(?:[-_. ]?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+)))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])(?:$|\.mkv)", Options)]
    private static partial Regex AnimeTitleTrailingNumberAbsoluteEpisodeRegex { get; }

    // Anime - Title Absolute Episode Number [Hash]
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:_|-|\s|\.)+(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+)))+(?:[-_. ]+(?<special>special|ova|ovd))?[-_. ]+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])$", Options)]
    private static partial Regex AnimeTitleAbsoluteEpisodeNumberHashRegex { get; }

    // Episodes with airdate AND season/episode number, capture season/episode only
    [GeneratedRegex(@"^(?<title>.+?)?\W*(?<airdate>\d{4}\W+[0-1][0-9]\W+[0-3][0-9])(?!\W+[0-3][0-9])[-_. ](?:s?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+)))(?:[ex](?<episode>(?<!\d+)(?:\d{1,3})(?!\d+)))", Options)]
    private static partial Regex EpisodesAirdateSeasonEpisodeNumberCaptureRegex { get; }

    // Episodes with airdate AND season/episode number
    [GeneratedRegex(@"^(?<title>.+?)?\W*(?<airyear>\d{4})\W+(?<airmonth>[0-1][0-9])\W+(?<airday>[0-3][0-9])(?!\W+[0-3][0-9]).+?(?:s?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+)))(?:[ex](?<episode>(?<!\d+)(?:\d{1,3})(?!\d+)))", Options)]
    private static partial Regex EpisodesAirdateSeasonEpisodeNumberRegex { get; }

    // Episodes with absolute episode number AND airdate (TJET Wrestling)
    [GeneratedRegex(@"^(?<title>.+?)?[-_. ](?:e\d{2,3}(?!\d+))[-_. ](?<airyear>\d{4})\W+(?<airmonth>[0-1][0-9])\W+(?<airday>[0-3][0-9])(?!\W+[0-3][0-9]).+?(?:\[[a-z]+\])", Options)]
    private static partial Regex EpisodesAbsoluteEpisodeNumberAirdateRegex { get; }

    // Single or multi episode releases with multiple titles, then season and episode numbers after the last title. (Title1 / Title2 / ... / S1E1-2 of 6)
    [GeneratedRegex(@"^((?<title>.*?)[ ._]\/[ ._])+\(?S(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:\W|_)?E?[ ._]?(?<episode>(?<!\d+)\d{1,2}(?!\d+))(?:-(?<episode>(?<!\d+)\d{1,2}(?!\d+)))?(?:[ ._]of[ ._](?<episodecount>\d{1,2}))?\)?[ ._][\(\[]", Options)]
    private static partial Regex SingleMultiEpisodeReleasesMultipleTitlesRegex { get; }

    // Multi-episode with title (S01E99-100, S01E05-06)
    [GeneratedRegex(@"^(?<title>.+?)(?:[-_\W](?<![()\[!]))+S(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))E(?<episode>\d{2,3}(?!\d+))(?:-(?<episode>\d{2,3}(?!\d+)))+(?:[-_. ]|$)", Options)]
    private static partial Regex MultiEpisodeTitleRegex { get; }

    // Multi-episode with title (S01E05-06, S01E05-6)
    [GeneratedRegex(@"^(?<title>.+?)(?:[-_\W](?<![()\[!]))+S(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))E(?<episode>\d{1,2}(?!\d+))(?:-(?<episode>\d{1,2}(?!\d+)))+(?:[-_. ]|$)", Options)]
    private static partial Regex MultiEpisodeTitle2Regex { get; }

    // Episodes with a Chinese title and English title, Single episodes (S01E05, 1x05, etc) & Multi-episode (S01E05E06, S01E05-06, S01E05 E06, etc)
    [GeneratedRegex(@"^(?<title>[\u4E00-\u9FCC]+)[_. ](?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:[ex]|\W[ex]){1,2}(?<episode>\d{2,3}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+)))*)(?:[-_. ]|$)", Options)]
    private static partial Regex EpisodesChineseTitleEnglishTitleSingleRegex { get; }

    // Episodes with a title, Single episodes (S01E05, 1x05, etc) & Multi-episode (S01E05E06, S01E05-06, S01E05 E06, etc)
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:[ex]|\W[ex]){1,2}(?<episode>\d{2,3}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+)))*)(?:[-_. ]|$)", Options)]
    private static partial Regex EpisodesTitleSingleEpisodesMultiEpisodeRegex { get; }

    // Episodes with a title, 4 digit season number, Single episodes (S2016E05, etc) & Multi-episode (S2016E05E06, S2016E05-06, S2016E05 E06, etc)
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S(?<season>(?<!\d+)(?:\d{4})(?!\d+))(?:e|\We|_){1,2}(?<episode>\d{2,4}(?!\d+))(?:(?:\-|e|\We|_){1,2}(?<episode>\d{2,3}(?!\d+)))*)\W?(?!\\)", Options)]
    private static partial Regex EpisodesTitle4DigitSeasonNumberRegex { get; }

    // Episodes with a title, 4 digit season number, Single episodes (2016x05, etc) & Multi-episode (2016x05x06, 2016x05-06, 2016x05 x06, etc)
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+(?<season>(?<!\d+)(?:\d{4})(?!\d+))(?:x|\Wx){1,2}(?<episode>\d{2,4}(?!\d+))(?:(?:\-|x|\Wx|_){1,2}(?<episode>\d{2,3}(?!\d+)))*)\W?(?!\\)", Options)]
    private static partial Regex EpisodesTitle4DigitSeasonNumber2Regex { get; }

    // Episodes with a title, Single episodes (s01.05)
    [GeneratedRegex(@"^(?<title>.+?)(?:[-_\W](?<![()\[!]))+S(?<season>(?<!\d+)(?:\d{2})(?!\d+))(?:\.)(?<episode>\d{2,3}(?!\d+))(?:[-_. ]|$)", Options)]
    private static partial Regex EpisodesTitleSingleEpisodesRegex { get; }

    // Multi-season pack
    [GeneratedRegex(@"^(?<title>.+?)(Complete Series)?[-_. ]+(?:S|(?:Season|Saison|Series|Stagione)[_. ])(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:(?:[-_. ]{1}|[-_. ]{3})(?:S|(?:Season|Saison|Series|Stagione)[_. ])?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))){1,}", Options)]
    private static partial Regex MultiSeasonPackRegex { get; }

    // Partial season pack
    [GeneratedRegex(@"^(?<title>.+?)(?:\W+S(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))\W+(?:(?:(?:Part|Vol)\W?|(?<!\d+\W+)e|p)(?<seasonpart>\d{1,2}(?!\d+)))+)", Options)]
    private static partial Regex PartialSeasonPackRegex { get; }

    // Anime - 4 digit absolute episode number in batch (1017-1088) or 1017-1088
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>.+?)[-_. ]+?\(?(?<absoluteepisode>\d{4}(\.\d{1,2})?(?!\d+))[-](?<absoluteepisode>\d{4}(\.\d{1,2})?(?!\d+))\)?", Options)]
    private static partial Regex Anime4DigitAbsoluteEpisodeNumberRegex { get; }

    // Anime - 4 digit absolute episode number
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>.+?)[-_. ]+?(?<absoluteepisode>\d{4}(\.\d{1,2})?(?!\d+))", Options)]
    private static partial Regex Anime4DigitAbsoluteEpisodeNumber2Regex { get; }

    // Mini-Series, treated as season 1, multi episodes are labelled as E1-E2
    [GeneratedRegex(@"^(?<title>.+?)(?:[-._ ][e])(?<episode>\d{2,3}(?!\d+))(?:(?:\-?[e])(?<episode>\d{2,3}(?!\d+)))+", Options)]
    private static partial Regex MiniSeriesTreatedAsSeason1Regex { get; }

    // 5 digit episode number with a title
    [GeneratedRegex(@"^(?:(?<title>.+?)(?:_|-|\s|\.)+)(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+)))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>(?<!\d+)\d{5}(?!\d+)))", Options)]
    private static partial Regex N5DigitEpisodeNumberTitleRegex { get; }

    // 5 digit multi-episode with a title
    [GeneratedRegex(@"^(?:(?<title>.+?)(?:_|-|\s|\.)+)(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+)))(?:(?:[-_. ]{1,3}ep){1,2}(?<episode>(?<!\d+)\d{5}(?!\d+)))+", Options)]
    private static partial Regex N5DigitMultiEpisodeTitleRegex { get; }

    // Episodes with airdate and part (2018.04.28.Part.2)
    [GeneratedRegex(@"^(?<title>.+?)?\W*(?<airyear>\d{4})[-_. ]+(?<airmonth>[0-1][0-9])[-_. ]+(?<airday>[0-3][0-9])(?![-_. ]+[0-3][0-9])[-_. ]+Part[-_. ]?(?<part>[1-9])", Options)]
    private static partial Regex EpisodesAirdatePartRegex { get; }

    // Episodes with airdate (2018.04.28)
    [GeneratedRegex(@"^(?<title>.+?)?\W*(?<airyear>\d{4})[-_. ]+(?<airmonth>[0-1][0-9])[-_. ]+(?<airday>[0-3][0-9])(?![-_. ]+[0-3][0-9])", Options)]
    private static partial Regex EpisodesAirdateRegex { get; }

    // Anime - Title 4-digit Absolute Episode Number [SubGroup]
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+(?<absoluteepisode>(?<!\d+)\d{4}(?!\d+))[-_. ]\[(?<subgroup>.+?)\]", Options)]
    private static partial Regex AnimeTitle4DigitAbsoluteEpisodeRegex { get; }

    // Mini-Series with year in title, treated as season 1, episodes are labelled as Part01, Part 01, Part.1
    [GeneratedRegex(@"^(?<title>.+?\d{4})(?:\W+(?:(?:Part\W?|e)(?<episode>\d{1,2}(?!\d+)))+)", Options)]
    private static partial Regex MiniSeriesYearTitleTreatedAsRegex { get; }

    // 3-digit season number
    // Single episode: Title.S001E01
    // Multi episode: Title.S001E01-E02
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+S(?<season>(?<!\d+)\d{3}(?!\d+))E(?<episode>(?<!\d+)\d{2}(?!\d+))(?:-[ex](?<episode>(?<!\d+)\d{2}(?!\d+)))?([_. ]|$)(?!\\)", Options)]
    private static partial Regex N3DigitSeasonNumberRegex { get; }

    // Mini-Series, treated as season 1, episodes are labelled as XofY
    [GeneratedRegex(@"^(?<title>.+?)(?:\W+(?:(?<episode>(?<!\d+)\d{1,2}(?!\d+))of\d+)+)", Options)]
    private static partial Regex MiniSeriesTreatedAsSeason12Regex { get; }

    // Supports Season 01 Episode 03
    [GeneratedRegex(@"(?:.*(?:\""|^))(?<title>.*?)(?:[-_\W](?<![()\[]))+(?:\W?Season\W?)(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:\W|_)+(?:Episode\W)(?:[-_. ]?(?<episode>(?<!\d+)\d{1,2}(?!\d+)))+", Options)]
    private static partial Regex SupportsSeason01Episode03Regex { get; }

    // Multi-episode with episodes in square brackets (Series Title [S01E11E12] or Series Title [S01E11-12])
    [GeneratedRegex(@"(?:.*(?:^))(?<title>.*?)[-._ ]+\[S(?<season>(?<!\d+)\d{2}(?!\d+))(?:[E-]{1,2}(?<episode>(?<!\d+)\d{2}(?!\d+)))+\]", Options)]
    private static partial Regex MultiEpisodeEpisodesSquareBracketsRegex { get; }

    // Multi-episode with episodes in brackets (Series Title (S01E11E12) or Series Title (S01E11-12) or Series Title (S01E1-3 of 12))
    [GeneratedRegex(@"(?:.*(?:^))(?<title>.*?)[-._ ]+\(S(?<season>(?<!\d+)\d{2}(?!\d+))(?:[E-]{1,2}(?<episode>(?<!\d+)\d{1,2}(?!\d+)))+([._ ]+of[._ ]+[\dx]+)?\)", Options)]
    private static partial Regex MultiEpisodeEpisodesBracketsSeriesTitleRegex { get; }

    // Multi-episode release with no space between series title and season (S01E11E12)
    [GeneratedRegex(@"(?:.*(?:^))(?<title>.*?)S(?<season>(?<!\d+)\d{2}(?!\d+))(?:E(?<episode>(?<!\d+)\d{2}(?!\d+)))+", Options)]
    private static partial Regex MultiEpisodeReleaseNoSpaceBetweenRegex { get; }

    // Multi-episode with single episode numbers (S6.E1-E2, S6.E1E2, S6E1E2, etc)
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]S(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:[-_. ]?[ex]?(?<episode>(?<!\d+)\d{1,2}(?!\d+)))+", Options)]
    private static partial Regex MultiEpisodeSingleEpisodeNumbersRegex { get; }

    // Single or multi episode releases with multiple titles, each followed by season and episode numbers in brackets
    [GeneratedRegex(@"^(?<title>.*?)[ ._]\(S(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:\W|_)?E?[ ._]?(?<episode>(?<!\d+)\d{1,2}(?!\d+))(?:-(?<episode>(?<!\d+)\d{1,2}(?!\d+)))?\)(?:[ ._]\/[ ._])(?<title>.*?)[ ._]\(", Options)]
    private static partial Regex SingleMultiEpisodeReleasesMultipleTitles2Regex { get; }

    // Multi-episode with episodes in brackets (Series Title (S01E11E12) or Series Title (S1E11-12) or Series Title (S1E1-3 of 12))
    [GeneratedRegex(@"(?:.*(?:^))(?<title>.*?)[-._ ]+\(S(?<season>(?<!\d+)\d{1}(?!\d+))(?:[E-]{1,2}(?<episode>(?<!\d+)\d{1,2}(?!\d+)))+([._ ]+of[._ ]+[\dx]+)?\)", Options)]
    private static partial Regex MultiEpisodeEpisodesBracketsSeriesTitle2Regex { get; }

    // Single episode season or episode S1E1 or S1-E1 or S1.Ep1 or S01.Ep.01
    [GeneratedRegex(@"(?:.*(?:\""|^))(?<title>.*?)(?:\W?|_)S(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:\W|_)?Ep?[ ._]?(?<episode>(?<!\d+)\d{1,2}(?!\d+))", Options)]
    private static partial Regex SingleEpisodeSeasonEpisodeS1e1S1Regex { get; }

    // 3 digit season S010E05
    [GeneratedRegex(@"(?:.*(?:\""|^))(?<title>.*?)(?:\W?|_)S(?<season>(?<!\d+)\d{3}(?!\d+))(?:\W|_)?E(?<episode>(?<!\d+)\d{1,2}(?!\d+))", Options)]
    private static partial Regex N3DigitSeasonS010e05Regex { get; }

    // Separated season and episode numbers S01 - E01
    [GeneratedRegex(@"^(?<title>.+?)(?:_|-|\s|\.)+S(?<season>\d{2}(?!\d+))(\W-\W)E(?<episode>(?<!\d+)\d{2}(?!\d+))(?!\\)", Options)]
    private static partial Regex SeparatedSeasonEpisodeNumbersS01E01Regex { get; }

    // Season and episode numbers in square brackets (single and multi-episode)
    // Series Title - [02x01] - Episode 1
    // Series Title - [02x01x02] - Episode 1
    [GeneratedRegex(@"^(?<title>.+?)?(?:[-_\W](?<![()\[!]))+\[(?:s)?(?<season>(?<!\d+)\d{1,2})(?:(?:[ex])(?<episode>\d{2}))(?:(?:[-ex]){1,2}(?<episode>\d{2}))*\].+?(?:\.|$)", Options)]
    private static partial Regex SeasonEpisodeNumbersSquareBracketsRegex { get; }

    // Anime - Title with season number - Absolute Episode Number (Title S01 - EP14)
    [GeneratedRegex(@"^(?<title>.+?S\d{1,2})[-_. ]{3,}(?:EP)?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+|[-]))", Options)]
    private static partial Regex AnimeTitleSeasonNumberAbsoluteEpisodeRegex { get; }

    // Anime - French titles with single episode numbers, with or without leading sub group ([RlsGroup] Title - Episode 1)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)[-_. ]+?(?:Episode[-_. ]+?)(?<absoluteepisode>\d{1}(\.\d{1,2})?(?!\d+))", Options)]
    private static partial Regex AnimeFrenchTitlesSingleEpisodeNumbersRegex { get; }

    // Anime - Absolute episode number in square brackets
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>.+?)[-_. ]+?\[(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+))\]", Options)]
    private static partial Regex AnimeAbsoluteEpisodeNumberSquareBracketsRegex { get; }

    // Japanese variety shows with leading date
    [GeneratedRegex(@"^(?<airyear>\d{2})(?<airmonth>[0-1][0-9])(?<airday>[0-3][0-9])(?![-_. ]+[0-3][0-9])[-_. ](?<title>.+?)[-_. ](?:Season[-_. ]?(?<season>\d{1,2})[-_. ])?(?:ep|#)(?<episode>\d{2,3})", Options)]
    private static partial Regex JapaneseVarietyShowsLeadingDateRegex { get; }

    // Season only releases followed by year
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+?(?:S|Season|Saison|Series|Stagione)[-_. ]?(?<season>\d{1,2}(?=[-_. ]\d{4}[-_. ]+))(?<extras>EXTRAS|SUBPACK)?(?!\\)", Options)]
    private static partial Regex SeasonOnlyReleasesFollowedYearRegex { get; }

    // Season only releases
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+?(?:S|Season|Saison|Series|Stagione)[-_. ]?(?<season>\d{1,2}(?![-_. ]?\d+))(?:[-_. ]|$)+(?<extras>EXTRAS|SUBPACK)?(?!\\)", Options)]
    private static partial Regex SeasonOnlyReleasesRegex { get; }

    // 4 digit season only releases
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+?(?:S|Season|Saison|Series|Stagione)[-_. ]?(?<season>\d{4}(?![-_. ]?\d+))(\W+|_|$)(?<extras>EXTRAS|SUBPACK)?(?!\\)", Options)]
    private static partial Regex N4DigitSeasonOnlyReleasesRegex { get; }

    // Spanish tracker releases
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_. ]+?Temporada.+?|\[.+?\])\[Cap)(?:[-_. ]+(?<season>(?<!\d+)\d{1,2})(?<episode>(?<!e|x)(?:[1-9][0-9]|[0][1-9])))+(?:\])", Options)]
    private static partial Regex SpanishTrackerReleasesRegex { get; }

    // Supports 103/113 naming
    [GeneratedRegex(@"^(?<title>.+?)?(?:(?:[_.-](?<![()\[!]))+(?<season>(?<!\d+)[1-9])(?<episode>[1-9][0-9]|[0][1-9])(?![a-z]|\d+))+(?:[_.]|$)", Options)]
    private static partial Regex Supports103113NamingRegex { get; }

    // 4 digit episode number
    // Episodes without a title, Single (S01E05, 1x05) AND Multi (S01E04E05, 1x04x05, etc)
    [GeneratedRegex(@"^(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{4}(?!\d+|i|p)))+)(\W+|_|$)(?!\\)", Options)]
    private static partial Regex N4DigitEpisodeNumberRegex { get; }

    // 4 digit episode number
    // Episodes with a title, Single episodes (S01E05, 1x05, etc) & Multi-episode (S01E05E06, S01E05-06, S01E05 E06, etc)
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]|\d{1,2}-))+S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{4}(?!\d+|i|p)))+)\W?(?!\\)", Options)]
    private static partial Regex N4DigitEpisodeNumber2Regex { get; }

    // Turkish tracker releases (01 BLM, 3. Blm, 04.Bolum, etc)
    [GeneratedRegex(@"^(?<title>.+?)[_. ](?<absoluteepisode>\d{1,4})(?:[_. ]+)(?:BLM|B[oö]l[uü]m)", Options)]
    private static partial Regex TurkishTrackerReleasesRegex { get; }

    // Episodes with airdate (04.28.2018)
    [GeneratedRegex(@"^(?<title>.+?)?\W*(?<ambiguousairmonth>[0-1][0-9])[-_. ]+(?<ambiguousairday>[0-3][0-9])[-_. ]+(?<airyear>\d{4})(?!\d+)", Options)]
    private static partial Regex EpisodesAirdate2Regex { get; }

    // Episodes with airdate (28.04.2018)
    [GeneratedRegex(@"^(?<title>.+?)?\W*(?<ambiguousairday>[0-3][0-9])[-_. ]+(?<ambiguousairmonth>[0-1][0-9])[-_. ]+(?<airyear>\d{4})(?!\d+)", Options)]
    private static partial Regex EpisodesAirdate3Regex { get; }

    // Episodes with airdate (20180428)
    [GeneratedRegex(@"^(?<title>.+?)?\W*(?<!\d+)(?<airyear>\d{4})(?<airmonth>[0-1][0-9])(?<airday>[0-3][0-9])(?!\d+)", Options)]
    private static partial Regex EpisodesAirdate4Regex { get; }

    // Anime - Title Absolute Episode Number (E195 or E1206)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:(?:_|-|\s|\.)+(?:e|ep)(?<absoluteepisode>(\d{3}|\d{4})(\.\d{1,2})?))+[-_. ].*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeTitleAbsoluteEpisodeNumberRegex { get; }

    // Daily episodes that use short month format instead of number
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+(?<airday>[1-2]\d|3[01]|[1-9])(?:th|st|rd)[-_. ](?<shortairmonth>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[-_. ](?<airyear>19[4-9]\d|20\d\d)", Options)]
    private static partial Regex DailyEpisodesThatUseShortMonthRegex { get; }

    // Supports 1103/1113 naming
    [GeneratedRegex(@"^(?<title>.+?)?(?:(?:[-_. ](?<![()\[!]))*(?<!\d{1,2}-)(?<season>(?<!\d+|\(|\[|e|x)\d{2})(?<episode>(?<!e|x)(?:[1-9][0-9]|[0][1-9])(?!p|i|\d+|\)|\]|\W\d+|\W(?:e|ep|x)\d+)))+([-_. ]+|$)(?!\\)", Options)]
    private static partial Regex Supports11031113NamingRegex { get; }

    // Dutch/Flemish release titles
    [GeneratedRegex(@"^(?<title>.+?)[-_. ](?:Se\.(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:[-_ ]?afl\.)(?<episode>\d{1,3}(?!\d+))(?:(?:[-]|[-_ ]en[-_ ])(?<episode>\d{1,3}(?!\d+)))*))", Options)]
    private static partial Regex DutchFlemishReleaseTitlesRegex { get; }

    // Episodes with single digit episode number (S01E1, S01E5E6, etc)
    [GeneratedRegex(@"^(?<title>.*?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]){1,2}(?<episode>\d{1}))+)+(\W+|_|$)(?!\\)", Options)]
    private static partial Regex EpisodesSingleDigitEpisodeNumberRegex { get; }

    // iTunes Season 1\05 Title (Quality).ext
    [GeneratedRegex(@"^(?:Season(?:_|-|\s|\.)(?<season>(?<!\d+)\d{1,2}(?!\d+)))(?:_|-|\s|\.)(?<episode>(?<!\d+)\d{1,2}(?!\d+))", Options)]
    private static partial Regex ItunesSeason105TitleExtRegex { get; }

    // iTunes 1-05 Title (Quality).ext
    [GeneratedRegex(@"^(?:(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:-(?<episode>\d{2,3}(?!\d+))))", Options)]
    private static partial Regex Itunes105TitleExtRegex { get; }

    // Mini-Series, treated as season 1, episodes are labelled as Part01, Part 01, Part.1
    [GeneratedRegex(@"^(?<title>.+?)(?:\W+(?:(?:(?<!\()Part\W?|(?<!\d+\W+)e)(?<episode>\d{1,2}(?!\d+|\))))+)", Options)]
    private static partial Regex MiniSeriesTreatedAsSeason13Regex { get; }

    // Mini-Series, treated as season 1, episodes are labelled as Part One/Two/Three/...Nine, Part.One, Part_One
    [GeneratedRegex(@"^(?<title>.+?)(?:\W+(?:Part[-._ ](?<episode>One|Two|Three|Four|Five|Six|Seven|Eight|Nine)(?>[-._ ])))", Options)]
    private static partial Regex MiniSeriesTreatedAsSeason14Regex { get; }

    // Anime Range - Title Absolute Episode Number (ep01-12)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:_|\s|\.)+(?:e|ep)(?<absoluteepisode>\d{2,3}(\.\d{1,2})?)-(?<absoluteepisode>(?<!\d+)\d{1,2}(\.\d{1,2})?(?!\d+|-)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeRangeTitleAbsoluteEpisodeNumberRegex { get; }

    // Anime - Title Absolute Episode Number (e66)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:(?:_|-|\s|\.)+(?:e|ep)(?<absoluteepisode>\d{2,4}(\.\d{1,2})?))+[-_. ].*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeTitleAbsoluteEpisodeNumber2Regex { get; }

    // Anime - Title Episode Absolute Episode Number (Series Title Episode 01)
    [GeneratedRegex(@"^(?<title>.+?)[-_. ](?:Episode)(?:[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+)))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeTitleEpisodeAbsoluteEpisodeNumberRegex { get; }

    // Anime Range - Title Absolute Episode Number (1 or 2 digit absolute episode numbers in a range, 1-10)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)[_. ]+(?<absoluteepisode>(?<!\d+)\d{1,2}(\.\d{1,2})?(?!\d+))-(?<absoluteepisode>(?<!\d+)\d{1,2}(\.\d{1,2})?(?!\d+|-)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeRangeTitleAbsoluteEpisodeNumber2Regex { get; }

    // Anime - Title Episode/Episodio Absolute Episode Number
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)[-_. ]+(?:Episode|Episodio)(?:[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,4}(\.\d{1,2})?(?!\d+|[ip])))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeTitleEpisodeEpisodioAbsoluteEpisodeRegex { get; }

    // Anime - Title [Absolute Episode Number] from AniLibriaTV
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:[-_. ]\[)(?:(?:-?)(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+|[ip])))+(?:\][-_. ]).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeTitleAbsoluteEpisodeNumberFromRegex { get; }

    // Simple season releases in the format 'Series (year) - Temporada season'
    [GeneratedRegex(@"^(?<title>.+?)[_. ]-[_. ](?:Temporada)[-_. ]?(?<season>\d{1,2}(?![_. ]?\d+))(?:[_. ]|$)+(?<extras>EXTRAS|SUBPACK)?(?!\\)", Options)]
    private static partial Regex SimpleSeasonReleasesFormatSeriesTemporadaRegex { get; }

    // Anime - Title Absolute Episode Number
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,4}(\.\d{1,2})?(?!\d+|[ip])))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeTitleAbsoluteEpisodeNumber3Regex { get; }

    // Anime - Title {Absolute Episode Number}
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+|[ip])))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", Options)]
    private static partial Regex AnimeTitleAbsoluteEpisodeNumber4Regex { get; }

    // Extant, terrible multi-episode naming (extant.10708.hdtv-lol.mp4)
    [GeneratedRegex(@"^(?<title>.+?)[-_. ](?<season>[0]?\d?)(?:(?<episode>\d{2}){2}(?!\d+))[-_. ]", Options)]
    private static partial Regex ExtantTerribleMultiEpisodeNamingRegex { get; }

    // Episodes without a title, Single episode numbers (S1E1, 1x1)
    [GeneratedRegex(@"^(?:S?(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:[-_ ]?[ex])(?<episode>\d{1}(?!\d+))))", Options)]
    private static partial Regex EpisodesWithoutTitleSingleEpisodeNumbersRegex { get; }

    // Anime OVA special
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)(?:[-_. ]+(?<special>special|ova|ovd|ncop|nced)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", Options)]
    private static partial Regex AnimeOvaSpecialRegex { get; }

    // Episodes with a title, 3 digit season number, Single episodes (S001E05, 1x05, etc) & Multi-episode (S001E05E06, S001E05-06, S001E05 E06, etc)
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{3})(?!\d+))(?:[ex]|\W[ex]){1,2}(?<episode>\d{2,3}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+)))*)(?:[-_. ]|$)", Options)]
    private static partial Regex EpisodesTitle3DigitSeasonNumberRegex { get; }
}
