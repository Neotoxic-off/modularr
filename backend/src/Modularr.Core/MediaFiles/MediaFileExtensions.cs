using System.Collections.Frozen;
using Modularr.Core.Qualities;

namespace Modularr.Core.MediaFiles;

public static class MediaFileExtensions
{
    private static readonly FrozenDictionary<string, Quality> QualityByExtension = new Dictionary<string, Quality>
    {
        [".webm"] = Quality.Unknown,

        [".m4v"] = Quality.Sdtv,
        [".3gp"] = Quality.Sdtv,
        [".nsv"] = Quality.Sdtv,
        [".ty"] = Quality.Sdtv,
        [".strm"] = Quality.Sdtv,
        [".rm"] = Quality.Sdtv,
        [".rmvb"] = Quality.Sdtv,
        [".m3u"] = Quality.Sdtv,
        [".ifo"] = Quality.Sdtv,
        [".mov"] = Quality.Sdtv,
        [".qt"] = Quality.Sdtv,
        [".divx"] = Quality.Sdtv,
        [".xvid"] = Quality.Sdtv,
        [".bivx"] = Quality.Sdtv,
        [".nrg"] = Quality.Sdtv,
        [".pva"] = Quality.Sdtv,
        [".wmv"] = Quality.Sdtv,
        [".asf"] = Quality.Sdtv,
        [".asx"] = Quality.Sdtv,
        [".ogm"] = Quality.Sdtv,
        [".ogv"] = Quality.Sdtv,
        [".m2v"] = Quality.Sdtv,
        [".avi"] = Quality.Sdtv,
        [".bin"] = Quality.Sdtv,
        [".dat"] = Quality.Sdtv,
        [".dvr-ms"] = Quality.Sdtv,
        [".mpg"] = Quality.Sdtv,
        [".mpeg"] = Quality.Sdtv,
        [".mp4"] = Quality.Sdtv,
        [".avc"] = Quality.Sdtv,
        [".vp3"] = Quality.Sdtv,
        [".svq3"] = Quality.Sdtv,
        [".nuv"] = Quality.Sdtv,
        [".viv"] = Quality.Sdtv,
        [".dv"] = Quality.Sdtv,
        [".fli"] = Quality.Sdtv,
        [".flv"] = Quality.Sdtv,
        [".wpl"] = Quality.Sdtv,

        [".img"] = Quality.Dvd,
        [".iso"] = Quality.Dvd,
        [".vob"] = Quality.Dvd,

        // Guess low: a file guessed HDTV still gets upgraded to a real WEB-DL, one guessed WEB-DL would not.
        [".mkv"] = Quality.Hdtv720p,
        [".mk3d"] = Quality.Hdtv720p,
        [".ts"] = Quality.Hdtv720p,
        [".wtv"] = Quality.Hdtv720p,

        [".m2ts"] = Quality.Bluray720p,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static bool IsMediaFile(string extension) => QualityByExtension.ContainsKey(extension);
    public static FrozenSet<string> VideoExtensions { get; } = QualityByExtension.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static Quality GetQualityForExtension(string extension) =>
        QualityByExtension.GetValueOrDefault(extension, Quality.Unknown);

    public static bool IsMediaExtension(string extension) => QualityByExtension.ContainsKey(extension);
}
