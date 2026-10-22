using System.Globalization;
using System.Text.RegularExpressions;
using Modularr.Core.MediaFiles;

namespace Modularr.Core.Parsing.Movies;

// shortcut: private copies of the release group, website and file extension helpers the episode parser owns, collapse them into the shared ones once both parsers are merged.
public static partial class MovieTitleParser
{
    [GeneratedRegex(@"^(?:(?:\[|\()\s*)?(?:www\.)?[-a-z0-9-]{1,256}\.(?<!Naruto-Kun\.)(?:[a-z]{2,6}\.[a-z]{2,6}|xn--[a-z0-9-]{4,}|[a-z]{2,})\b(?:\s*(?:\]|\))|[ -]{2,})[ -]*", Options)]
    private static partial Regex WebsitePrefixRegex { get; }

    [GeneratedRegex(@"(?:\[\s*)?(?:www\.)?[-a-z0-9-]{1,256}\.(?:xn--[a-z0-9-]{4,}|[a-z]{2,6})\b(?:\s*\])$", Options)]
    private static partial Regex WebsitePostfixRegex { get; }

    [GeneratedRegex(@"\[(?:ettv|rartv|rarbg|cttv|publichd)\]$", Options)]
    private static partial Regex CleanTorrentSuffixRegex { get; }

    [GeneratedRegex(@"\.[a-z0-9]{2,4}$", Options)]
    private static partial Regex FileExtensionRegex { get; }

    [GeneratedRegex(@"-(?<releasegroup>[a-z0-9]+(?<part2>-[a-z0-9]+)?(?!.+?(?:480p|576p|720p|1080p|2160p)))(?<!(?:WEB-(DL|Rip)|Blu-Ray|480p|576p|720p|1080p|2160p|DTS-HD|DTS-X|DTS-MA|DTS-ES|-ES|-EN|-CAT|-ENG|-JAP|-GER|-FRA|-FRE|-ITA|-HDRip|\d{1,2}-bit|[ ._]\d{4}-\d{2}|-\d{2}|tmdb(id)?-(?<tmdbid>\d+)|(?<imdbid>tt\d{7,8}))(?:\k<part2>)?)(?:\b|[-._ ]|$)|[-._ ]\[(?<releasegroup>[a-z0-9]+)\]$", Options)]
    private static partial Regex ReleaseGroupRegex { get; }

    [GeneratedRegex(@"^([se]\d+|[0-9a-f]{8})$", Options)]
    private static partial Regex InvalidReleaseGroupRegex { get; }

    [GeneratedRegex(@"^(?:\[(?<subgroup>(?!\s).+?(?<!\s))\](?:_|-|\s|\.)?)", Options)]
    private static partial Regex AnimeReleaseGroupRegex { get; }

    // Groups that do not follow -RlsGrp, matched by name only.
    [GeneratedRegex(@"\b(?<releasegroup>KRaLiMaRKo|E\.N\.D|D\-Z0N3|Koten_Gars|BluDragon|ZØNEHD|HQMUX|VARYG|YIFY|YTS(.(MX|LT|AG))?|TMd|Eml HDTeam|LMain|DarQ|BEN THE MEN|TAoE|QxR|126811)\b", Options)]
    private static partial Regex ExceptionReleaseGroupExactRegex { get; }

    // Groups whose releases end with RlsGroup) or RlsGroup].
    [GeneratedRegex(@"(?<=[._ \[])(?<releasegroup>(Silence|afm72|Panda|Ghost|MONOLITH|Tigole|Joy|ImE|UTR|t3nzin|Anime Time|Project Angel|Hakata Ramen|HONE|GiLG|Vyndros|SEV|Garshasp|Kappa|Natty|RCVR|SAMPA|YOGI|r00t|EDGE2020|RZeroX|FreetheFish|Anna|Bandi|Qman|theincognito|HDO|DusIctv|DHD|CtrlHD|-ZR-|ADC|XZVN|RH|Kametsu|Celdra)(?=\]|\)))", Options)]
    private static partial Regex ExceptionReleaseGroupRegex { get; }

    [GeneratedRegex(@"(-(RP|1|NZBGeek|Obfuscated|Obfuscation|Scrambled|sample|Pre|postbot|xpost|Rakuv[a-z0-9]*|WhiteRev|BUYMORE|AsRequested|AlternativeToRequested|GEROV|Z0iDS3N|Chamele0n|4P|4Planet|AlteZachen|RePACKPOST))+$", Options)]
    private static partial Regex CleanReleaseGroupRegex { get; }

    private static string? ParseReleaseGroup(string title)
    {
        title = RemoveFileExtension(title.Trim());
        title = WebsitePrefixRegex.Replace(title, string.Empty);
        title = CleanTorrentSuffixRegex.Replace(title, string.Empty);

        Match anime = AnimeReleaseGroupRegex.Match(title);

        if (anime.Success)
        {
            return anime.Groups["subgroup"].Value;
        }

        title = CleanReleaseGroupRegex.Replace(title, string.Empty);

        Match? match = ExceptionReleaseGroupExactRegex.Matches(title).LastOrDefault()
            ?? ExceptionReleaseGroupRegex.Matches(title).LastOrDefault();

        if (match is not null)
        {
            return match.Groups["releasegroup"].Value;
        }

        match = ReleaseGroupRegex.Matches(title).LastOrDefault();
        string? group = match?.Groups["releasegroup"].Value;

        return group is null || int.TryParse(group, CultureInfo.InvariantCulture, out _) || InvalidReleaseGroupRegex.IsMatch(group) ? null : group;
    }

    private static string RemoveFileExtension(string title) =>
        FileExtensionRegex.Replace(title, m => MediaFileExtensions.IsMediaExtension(m.Value) || m.Value.ToLowerInvariant() is ".par2" or ".nzb" ? string.Empty : m.Value);
}
