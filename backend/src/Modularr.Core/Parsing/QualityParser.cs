using System.Globalization;
using System.Text.RegularExpressions;
using Modularr.Core.MediaFiles;
using Modularr.Core.Qualities;

namespace Modularr.Core.Parsing;

public static partial class QualityParser
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"\b(?:
        (?<bluray>M?Blu[-_. ]?Ray|HD[-_. ]?DVD|BD(?!$)|UHD2?BD|BDISO|BDMux|BD25|BD50|BR[-_. ]?DISK)|
        (?<webdl>WEB[-_. ]?DL(?:mux)?|AmazonHD|AmazonSD|iTunesHD|MaxdomeHD|NetflixU?HD|WebHD|HBOMaxHD|DisneyHD|[. ]WEB[. ](?:[xh][ .]?26[456]|AVC|HEVC|EAC3|DDP?[ .]?5[. ]1)|[. ](?-i:WEB)$|(?:\d{2,4}0p)[-. ](?:Hybrid[-_. ]?)?WEB[-. ]|[-. ]WEB[-. ]\d{2,4}0p|\b\s\/\sWEB\s\/\s\b|(?:AMZN|ATVP|DP|NF)[. -]WEB[. -](?!Rip))|
        (?<webrip>WebRip|Web-Rip|WEBMux)|
        (?<hdtv>HDTV)|
        (?<bdrip>BDRip|BDLight|HD[-_. ]?DVDRip|UHDBDRip)|
        (?<brrip>BRRip)|
        (?<dvdr>\d?x?M?DVD-?[R59])|
        (?<dvd>DVD(?!-R)|DVDRip|xvidvd)|
        (?<standard>NTSC|PAL)|
        (?<dsr>WS[-_. ]DSR|DSR)|
        (?<regional>R[0-9]{1}|REGIONAL)|
        (?<scr>SCR|SCREENER|DVDSCR|DVDSCREENER)|
        (?<ts>TS[-_. ]|TELESYNCH?|HD-TS|HDTS|PDVD|TSRip|HDTSRip)|
        (?<tc>TC|TELECINE|HD-TC|HDTC)|
        (?<cam>CAMRIP|(?:NEW)?CAM|HD-?CAM(?:Rip)?|HQCAM)|
        (?<wp>WORKPRINT|WP)|
        (?<pdtv>PDTV)|
        (?<sdtv>SDTV)|
        (?<tvrip>TVRip)
        )(?:\b|$|[ .])", Options | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex SourceRegex { get; }

    private static readonly string[] SourceGroups =
        ["bluray", "webdl", "webrip", "hdtv", "bdrip", "brrip", "dvdr", "dvd", "standard", "dsr", "regional", "scr", "ts", "tc", "cam", "wp", "pdtv", "sdtv", "tvrip"];

    [GeneratedRegex(@"\b(?:RawHD|Raw[-_. ]HD)\b", Options)]
    private static partial Regex RawHdRegex { get; }

    [GeneratedRegex(@"\bMPEG[-_. ]?2\b")]
    private static partial Regex Mpeg2Regex { get; }

    [GeneratedRegex(@"^(?!.*\b((?<!HD[._ -]|HD)DVD|BDRip|720p|MKV|XviD|WMV|d3g|(BD)?REMUX|^(?=.*1080p)(?=.*HEVC)|[xh][-_. ]?26[45]|German.*[DM]L|((?<=\d{4}).*German.*([DM]L)?)(?=.*\b(AVC|HEVC|VC[-_. ]?1|MVC|MPEG[-_. ]?2)\b))\b)(((?=.*\b(Blu[-_. ]?ray|BD|HD[-_. ]?DVD)\b)(?=.*\b(AVC|HEVC|VC[-_. ]?1|MVC|MPEG[-_. ]?2|BDMV|ISO)\b))|^((?=.*\b(((?=.*\b((.*_)?COMPLETE.*|Dis[ck])\b)(?=.*(Blu[-_. ]?ray|HD[-_. ]?DVD)))|3D[-_. ]?BD|BR[-_. ]?DISK|Full[-_. ]?Blu[-_. ]?ray|^((?=.*((BD|UHD)[-_. ]?(25|50|66|100|ISO)))))))).*", Options)]
    private static partial Regex BrDiskRegex { get; }

    [GeneratedRegex(@"\bproper\b", Options)]
    private static partial Regex ProperRegex { get; }

    [GeneratedRegex(@"\b(?:repack\d?|rerip\d?)\b", Options)]
    private static partial Regex RepackRegex { get; }

    [GeneratedRegex(@"\d[-._ ]?v(?<version>\d)[-._ ]|\[v(?<version>\d)\]|repack(?<version>\d)|rerip(?<version>\d)|(?:480|576|720|1080|2160)p[._ ]v(?<version>\d)", Options)]
    private static partial Regex VersionRegex { get; }

    [GeneratedRegex(@"\bREAL\b")]
    private static partial Regex RealRegex { get; }

    [GeneratedRegex(@"\b(?:(?<R360p>360p)|(?<R480p>480p|480i|640x480|848x480)|(?<R540p>540p)|(?<R576p>576p)|(?<R720p>720p|1280x720|960p)|(?<R1080p>1080p|1920x1080|1440p|FHD|1080i|4kto1080p)|(?<R2160p>2160p|3840x2160|4k[-_. ](?:UHD|HEVC|BD|H\.?26[56])|(?:UHD|HEVC|BD|H\.?26[56])[-_. ]4k))\b", Options)]
    private static partial Regex ResolutionRegex { get; }

    // No resolution in the name: UHD or [4K] means 2160p.
    [GeneratedRegex(@"\bUHD\b|\[4K\]", Options)]
    private static partial Regex AlternativeResolutionRegex { get; }

    [GeneratedRegex(@"\b(?:(?<x264>x264)|(?<h264>h264)|(?<xvidhd>XvidHD)|(?<xvid>X-?vid)|(?<divx>divx))\b", Options)]
    private static partial Regex CodecRegex { get; }

    [GeneratedRegex(@"(?<hdtv>HD[-_. ]TV)|(?<sdtv>SD[-_. ]TV)", Options)]
    private static partial Regex OtherSourceRegex { get; }

    [GeneratedRegex(@"bd(?:720|1080|2160)|(?<=[-_. (\[])bd(?=[-_. )\]])", Options)]
    private static partial Regex AnimeBlurayRegex { get; }

    [GeneratedRegex(@"\[WEB\]|[\[\(]WEB[ .]", Options)]
    private static partial Regex AnimeWebDlRegex { get; }

    [GeneratedRegex(@"hr[-_. ]ws", Options)]
    private static partial Regex HighDefPdtvRegex { get; }

    [GeneratedRegex(@"(?:[_. \[]|\d{4}p-|\bHybrid-)(?:(?:BD|UHD)[-_. ]?)?Remux\b|(?:(?:BD|UHD)[-_. ]?)?Remux[_. ]\d{4}p", Options)]
    private static partial Regex RemuxRegex { get; }

    [GeneratedRegex(@"((?<=\d{4}).*German.*([DM]L)?)(?=.*\b(AVC|HEVC|VC[_. -]?1|MVC|MPEG[_. -]?2))(?=.*Blu-?ray)", Options)]
    private static partial Regex GermanRemuxRegex { get; }

    public static QualityModel ParseQuality(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new QualityModel(Quality.Unknown, new Revision());
        }

        name = name.Trim();
        QualityModel result = ParseQualityName(name);

        return result.Quality != Quality.Unknown
            ? result
            : result with
            {
                Quality = MediaFileExtensions.GetQualityForExtension(Path.GetExtension(name)),
                SourceDetectionSource = QualityDetectionSource.Extension,
                ResolutionDetectionSource = QualityDetectionSource.Extension,
                ModifierDetectionSource = QualityDetectionSource.Extension,
            };
    }

    public static QualityModel ParseQualityName(string name)
    {
        string normalized = name.Replace('_', ' ').Trim();
        QualityModel result = ParseRevision(name, normalized);

        // NTSC/PAL only name the source when nothing more precise does (DVD-R.NTSC is a DVD-R).
        MatchCollection sourceMatches = SourceRegex.Matches(normalized);
        Match? sourceMatch = sourceMatches.LastOrDefault(m => !m.Groups["standard"].Success) ?? sourceMatches.LastOrDefault();
        Resolution resolution = ParseResolution(normalized);
        Match codec = CodecRegex.Match(normalized);
        bool remux = RemuxRegex.IsMatch(normalized) || GermanRemuxRegex.IsMatch(normalized);
        bool brDisk = BrDiskRegex.IsMatch(normalized);

        if (RawHdRegex.IsMatch(normalized) && !brDisk)
        {
            return result with
            {
                Quality = Quality.RawHd,
                SourceDetectionSource = QualityDetectionSource.Name,
                ResolutionDetectionSource = QualityDetectionSource.Name,
            };
        }

        if (resolution != Resolution.Unknown)
        {
            result = result with { ResolutionDetectionSource = QualityDetectionSource.Name };
        }

        if (sourceMatch is not null)
        {
            string group = SourceGroups.First(g => sourceMatch.Groups[g].Success);
            return FromSource(group, result with { SourceDetectionSource = QualityDetectionSource.Name }, name, normalized, resolution, codec, remux, brDisk);
        }

        if (remux && resolution != Resolution.Unknown)
        {
            Quality? remuxQuality = resolution switch
            {
                Resolution.R480p => Quality.Bluray480p,
                Resolution.R720p => Quality.Bluray720p,
                Resolution.R1080p => Quality.Remux1080p,
                Resolution.R2160p => Quality.Remux2160p,
                _ => null,
            };

            if (remuxQuality is not null)
            {
                return result with { Quality = remuxQuality };
            }
        }

        if (AnimeBlurayRegex.IsMatch(normalized))
        {
            result = result with { SourceDetectionSource = QualityDetectionSource.Name };
            QualityModel named = result with { ResolutionDetectionSource = QualityDetectionSource.Name };

            if (IsSd(resolution) || Has(normalized, "480p"))
            {
                return named with { Quality = Quality.Dvd };
            }

            if (resolution == Resolution.R1080p || Has(normalized, "1080p"))
            {
                return named with { Quality = remux ? Quality.Remux1080p : Quality.Bluray1080p };
            }

            if (resolution == Resolution.R2160p || Has(normalized, "2160p"))
            {
                return named with { Quality = remux ? Quality.Remux2160p : Quality.Bluray2160p };
            }

            // A remux without a resolution is 1080p; a 720p remux stays Bluray-720p.
            return result with { Quality = remux && resolution != Resolution.R720p ? Quality.Remux1080p : Quality.Bluray720p };
        }

        if (AnimeWebDlRegex.IsMatch(normalized))
        {
            result = result with { SourceDetectionSource = QualityDetectionSource.Name };
            QualityModel named = result with { ResolutionDetectionSource = QualityDetectionSource.Name };

            if (IsSd(resolution) || Has(normalized, "480p"))
            {
                return named with { Quality = Quality.WebDl480p };
            }

            if (resolution == Resolution.R1080p || Has(normalized, "1080p"))
            {
                return named with { Quality = Quality.WebDl1080p };
            }

            if (resolution == Resolution.R2160p || Has(normalized, "2160p"))
            {
                return named with { Quality = Quality.WebDl2160p };
            }

            return result with { Quality = Quality.WebDl720p };
        }

        if (resolution != Resolution.Unknown)
        {
            return FromResolution(result, name, resolution, remux);
        }

        if (codec.Groups["x264"].Success)
        {
            return result with { Quality = Quality.Sdtv };
        }

        if (Has(normalized, "848x480"))
        {
            QualityModel named = result with { ResolutionDetectionSource = QualityDetectionSource.Name };

            return Has(normalized, "dvd") ? named with { Quality = Quality.Dvd, SourceDetectionSource = QualityDetectionSource.Name }
                : Has(normalized, "bluray") ? named with { Quality = Quality.Bluray480p, SourceDetectionSource = QualityDetectionSource.Name }
                : named with { Quality = Quality.Sdtv };
        }

        if (Has(normalized, "1280x720"))
        {
            QualityModel named = result with { ResolutionDetectionSource = QualityDetectionSource.Name };

            return Has(normalized, "bluray")
                ? named with { Quality = Quality.Bluray720p, SourceDetectionSource = QualityDetectionSource.Name }
                : named with { Quality = Quality.Hdtv720p };
        }

        if (Has(normalized, "1920x1080"))
        {
            QualityModel named = result with { ResolutionDetectionSource = QualityDetectionSource.Name };

            return Has(normalized, "bluray")
                ? named with { Quality = Quality.Bluray1080p, SourceDetectionSource = QualityDetectionSource.Name }
                : named with { Quality = Quality.Hdtv1080p };
        }

        Quality? glued = Has(normalized, "bluray720p") ? Quality.Bluray720p
            : Has(normalized, "bluray1080p") ? Quality.Bluray1080p
            : Has(normalized, "bluray2160p") ? Quality.Bluray2160p
            : null;

        if (glued is not null)
        {
            return result with
            {
                Quality = glued,
                SourceDetectionSource = QualityDetectionSource.Name,
                ResolutionDetectionSource = QualityDetectionSource.Name,
            };
        }

        Match other = OtherSourceRegex.Match(normalized);
        Quality? otherQuality = other.Groups["sdtv"].Success ? Quality.Sdtv
            : other.Groups["hdtv"].Success ? Quality.Hdtv720p
            : null;

        return otherQuality is null
            ? result
            : result with { Quality = otherQuality, SourceDetectionSource = QualityDetectionSource.Name };
    }

    private static QualityModel FromSource(string group, QualityModel result, string name, string normalized, Resolution resolution, Match codec, bool remux, bool brDisk) => group switch
    {
        "bluray" => result with { Quality = FromBluray(resolution, codec, remux, brDisk) },
        "webdl" => result with
        {
            Quality = resolution switch
            {
                Resolution.R2160p => Quality.WebDl2160p,
                Resolution.R1080p => Quality.WebDl1080p,
                Resolution.R720p => Quality.WebDl720p,
                _ => name.Contains("[WEBDL]", StringComparison.Ordinal) ? Quality.WebDl720p : Quality.WebDl480p,
            },
        },
        "webrip" => result with
        {
            Quality = resolution switch
            {
                Resolution.R2160p => Quality.WebRip2160p,
                Resolution.R1080p => Quality.WebRip1080p,
                Resolution.R720p => Quality.WebRip720p,
                _ => Quality.WebRip480p,
            },
        },
        "hdtv" => result with
        {
            Quality = Mpeg2Regex.IsMatch(normalized) ? Quality.RawHd : resolution switch
            {
                Resolution.R2160p => Quality.Hdtv2160p,
                Resolution.R1080p => Quality.Hdtv1080p,
                Resolution.R720p => Quality.Hdtv720p,
                _ => name.Contains("[HDTV]", StringComparison.Ordinal) ? Quality.Hdtv720p : Quality.Sdtv,
            },
        },
        "bdrip" or "brrip" => result with
        {
            Quality = resolution switch
            {
                Resolution.R2160p => Quality.Bluray2160p,
                Resolution.R1080p => Quality.Bluray1080p,
                Resolution.R720p => Quality.Bluray720p,
                Resolution.R576p => Quality.Bluray576p,
                _ => Quality.Bluray480p,
            },
        },
        "dvdr" => result with { Quality = Quality.DvdR },
        "dvd" or "standard" => result with { Quality = Quality.Dvd },
        "regional" => result with { Quality = Quality.Regional },
        "scr" => result with { Quality = Quality.DvdScr },
        "ts" => result with { Quality = Quality.Telesync with { Resolution = (int)resolution } },
        "tc" => result with { Quality = Quality.Telecine },
        "cam" => result with { Quality = Quality.Cam },
        "wp" => result with { Quality = Quality.Workprint },
        _ when resolution == Resolution.R1080p || Has(normalized, "1080p") => result with { Quality = Quality.Hdtv1080p },
        _ when resolution == Resolution.R720p || Has(normalized, "720p") => result with { Quality = Quality.Hdtv720p },
        _ when HighDefPdtvRegex.IsMatch(normalized) => result with { Quality = Quality.Hdtv720p, ResolutionDetectionSource = QualityDetectionSource.Name },
        _ => result with { Quality = Quality.Sdtv },
    };

    private static Quality FromBluray(Resolution resolution, Match codec, bool remux, bool brDisk)
    {
        if (brDisk)
        {
            return Quality.BrDisk;
        }

        if (codec.Groups["xvid"].Success || codec.Groups["divx"].Success)
        {
            return Quality.Bluray480p;
        }

        return resolution switch
        {
            Resolution.R2160p => remux ? Quality.Remux2160p : Quality.Bluray2160p,
            Resolution.R1080p => remux ? Quality.Remux1080p : Quality.Bluray1080p,
            Resolution.R720p => Quality.Bluray720p,
            Resolution.R576p => Quality.Bluray576p,
            Resolution.R360p or Resolution.R480p or Resolution.R540p => Quality.Bluray480p,
            // A remux without a resolution is 1080p.
            _ => remux ? Quality.Remux1080p : Quality.Bluray720p,
        };
    }

    private static QualityModel FromResolution(QualityModel result, string name, Resolution resolution, bool remux)
    {
        QualitySource source = QualitySource.Unknown;
        Modifier modifier = Modifier.None;

        if (remux)
        {
            result = result with { SourceDetectionSource = QualityDetectionSource.Name };
            source = QualitySource.Bluray;
            modifier = Modifier.Remux;
        }
        else
        {
            Quality fromExtension = MediaFileExtensions.GetQualityForExtension(Path.GetExtension(name));
            if (fromExtension != Quality.Unknown)
            {
                result = result with { SourceDetectionSource = QualityDetectionSource.Extension };
                source = fromExtension.Source;
            }
        }

        (int target, Quality fallback) = resolution switch
        {
            Resolution.R2160p => (2160, Quality.Hdtv2160p),
            Resolution.R1080p => (1080, Quality.Hdtv1080p),
            Resolution.R720p => (720, Quality.Hdtv720p),
            _ => (480, Quality.Sdtv),
        };

        return result with
        {
            ResolutionDetectionSource = QualityDetectionSource.Name,
            Quality = source == QualitySource.Unknown ? fallback : QualityFinder.Find(source, target, modifier),
        };
    }

    private static Resolution ParseResolution(string name)
    {
        Match match = ResolutionRegex.Match(name);
        if (match.Success)
        {
            foreach (Resolution resolution in Enum.GetValues<Resolution>())
            {
                if (match.Groups[resolution.ToString()].Success)
                {
                    return resolution;
                }
            }
        }

        return AlternativeResolutionRegex.IsMatch(name) ? Resolution.R2160p : Resolution.Unknown;
    }

    private static QualityModel ParseRevision(string name, string normalized)
    {
        Match versionMatch = VersionRegex.Match(normalized);
        int? parsed = versionMatch.Success ? int.Parse(versionMatch.Groups["version"].ValueSpan, CultureInfo.InvariantCulture) : null;
        bool detected = parsed is not null;
        int version = parsed ?? 1;
        bool isRepack = false;

        if (ProperRegex.IsMatch(normalized))
        {
            version = (parsed ?? 1) + 1;
            detected = true;
        }

        if (RepackRegex.IsMatch(normalized))
        {
            version = (parsed ?? 1) + 1;
            isRepack = true;
            detected = true;
        }

        // shortcut: REAL is counted anywhere in the name, upstream wants it only after the episode number; fix with the title parser.
        int real = RealRegex.Count(name);

        return new QualityModel(Quality.Unknown, new Revision(version, real, isRepack))
        {
            RevisionDetectionSource = detected || real > 0 ? QualityDetectionSource.Name : QualityDetectionSource.Unknown,
        };
    }

    private static bool IsSd(Resolution resolution) =>
        resolution is Resolution.R360p or Resolution.R480p or Resolution.R540p or Resolution.R576p;

    private static bool Has(string value, string needle) => value.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
