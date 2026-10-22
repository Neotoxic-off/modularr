using System.Globalization;
using System.Text.RegularExpressions;

namespace Modularr.Core.Parsing;

// Merged from Sonarr and Radarr ReleaseGroupParser: the exception lists and exclusions are the union of both.
public static partial class ReleaseGroupParser
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"-(?<releasegroup>[A-Za-zÀ-ÖØ-öø-ÿ0-9]+(?<part2>-[A-Za-zÀ-ÖØ-öø-ÿ0-9]+)?(?!.+?(?:HDTV|SDTV|480p|576p|720p|1080p|2160p)))(?<!(?:HDTV|SDTV|WEB-(?:DL|Rip)|Blu-Ray|480p|576p|720p|1080p|2160p|DTS-HD|DTS-X|DTS-MA|DTS-ES|-ES|-EN|-CAT|-ENG|-JAP|-GER|-FRA|-FRE|-ITA|-HDRip|\d{1,2}-bit|[ ._]\d{4}-\d{2}|-\d{2}|tmdb(?:id)?-\d+|tt\d{7,8})(?:\k<part2>)?)(?:\b|[-._ ]|$)|[-._ ]\[(?<releasegroup>[A-Za-zÀ-ÖØ-öø-ÿ0-9]+)\]$", Options)]
    private static partial Regex ReleaseGroupRegex { get; }

    [GeneratedRegex(@"^(?:[se]\d+|[0-9a-f]{8})$", Options)]
    private static partial Regex InvalidReleaseGroupRegex { get; }

    [GeneratedRegex(@"^(?:\[(?<subgroup>(?!\s).+?(?<!\s))\](?:_|-|\s|\.)?)", Options)]
    private static partial Regex AnimeReleaseGroupRegex { get; }

    // Groups that do not follow -RlsGrp, matched by name only: high chance of false positives, keep it short.
    [GeneratedRegex(@"\b(?<releasegroup>Fight-BB|VARYG|E\.N\.D|KRaLiMaRKo|BluDragon|DarQ|KCRT|BEN[_. ]THE[_. ]MEN|TAoE|QxR|Vialle|D\-Z0N3|Koten_Gars|ZØNEHD|HQMUX|YIFY|YTS(?:.(?:MX|LT|AG))?|TMd|Eml HDTeam|LMain|126811)\b", Options)]
    private static partial Regex ExceptionReleaseGroupExactRegex { get; }

    // Groups whose releases end with RlsGroup) or RlsGroup].
    [GeneratedRegex(@"(?<=[._ \[])(?<releasegroup>(?:Silence|afm72|Panda|Ghost|MONOLITH|Tigole|Joy|ImE|UTR|t3nzin|Anime Time|Project Angel|Hakata Ramen|HONE|GiLG|Vyndros|SEV|Garshasp|Kappa|Natty|RCVR|SAMPA|YOGI|r00t|EDGE2020|RZeroX|FreetheFish|Anna|Bandi|Qman|theincognito|HDO|DusIctv|DHD|CtrlHD|-ZR-|ADC|XZVN|RH|Kametsu|Celdra)(?=\]|\)))", Options)]
    private static partial Regex ExceptionReleaseGroupRegex { get; }

    [GeneratedRegex(@"^(?:.*?[-._ ](?:S\d+E\d+)[-._ ])|(?:-(?:RP|1|NZBGeek|\[N-Z-B\]|Obfuscated|Obfuscation|Scrambled|sample|Pre|postbot|xpost|Rakuv[a-z0-9]*|WhiteRev|BUYMORE|AsRequested|AlternativeToRequested|GEROV|Z0iDS3N|Chamele0n|4P|4Planet|AlteZachen|RePACKPOST))+$", Options)]
    private static partial Regex CleanReleaseGroupRegex { get; }

    public static string? ParseReleaseGroup(string title)
    {
        title = ReleaseTitle.RemoveFileExtension(title.Trim());
        title = ReleaseTitle.PreSubstituteFirst(title);
        title = ReleaseTitle.StripTorrentSuffix(ReleaseTitle.StripWebsitePrefix(title));

        Match anime = AnimeReleaseGroupRegex.Match(title);
        if (anime.Success)
        {
            return anime.Groups["subgroup"].Value;
        }

        title = CleanReleaseGroupRegex.Replace(title, "");

        // Radarr order: a trusted name wins over a bracketed one ("(... Tigole) [QxR]" is QxR).
        Match? exact = ExceptionReleaseGroupExactRegex.Matches(title).LastOrDefault();
        if (exact is not null)
        {
            return exact.Groups["releasegroup"].Value;
        }

        Match? bracketed = ExceptionReleaseGroupRegex.Matches(title).LastOrDefault();
        if (bracketed is not null)
        {
            return bracketed.Groups["releasegroup"].Value;
        }

        Match? match = ReleaseGroupRegex.Matches(title).LastOrDefault();
        if (match is null)
        {
            return null;
        }

        string group = match.Groups["releasegroup"].Value;
        return int.TryParse(group, CultureInfo.InvariantCulture, out _) || InvalidReleaseGroupRegex.IsMatch(group) ? null : group;
    }
}
