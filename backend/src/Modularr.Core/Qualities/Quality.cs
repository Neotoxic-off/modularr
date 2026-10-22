using System.Collections.Frozen;

namespace Modularr.Core.Qualities;

public sealed record Quality(int Id, string Name, QualitySource Source, int Resolution, Modifier Modifier = Modifier.None)
{
    public static readonly Quality Unknown = new(0, "Unknown", QualitySource.Unknown, 0);

    public static readonly Quality Workprint = new(24, "WORKPRINT", QualitySource.Workprint, 0);
    public static readonly Quality Cam = new(25, "CAM", QualitySource.Cam, 0);
    public static readonly Quality Telesync = new(26, "TELESYNC", QualitySource.Telesync, 0);
    public static readonly Quality Telecine = new(27, "TELECINE", QualitySource.Telecine, 0);
    public static readonly Quality DvdScr = new(28, "DVDSCR", QualitySource.Dvd, 480, Modifier.Screener);
    public static readonly Quality Regional = new(29, "REGIONAL", QualitySource.Dvd, 480, Modifier.Regional);

    public static readonly Quality Sdtv = new(1, "SDTV", QualitySource.Television, 480);
    public static readonly Quality Dvd = new(2, "DVD", QualitySource.Dvd, 0);
    public static readonly Quality DvdR = new(23, "DVD-R", QualitySource.Dvd, 480, Modifier.Remux);

    public static readonly Quality Hdtv720p = new(4, "HDTV-720p", QualitySource.Television, 720);
    public static readonly Quality Hdtv1080p = new(9, "HDTV-1080p", QualitySource.Television, 1080);
    public static readonly Quality Hdtv2160p = new(16, "HDTV-2160p", QualitySource.Television, 2160);
    public static readonly Quality RawHd = new(10, "Raw-HD", QualitySource.Television, 1080, Modifier.RawHd);

    public static readonly Quality WebDl480p = new(8, "WEBDL-480p", QualitySource.WebDl, 480);
    public static readonly Quality WebDl720p = new(5, "WEBDL-720p", QualitySource.WebDl, 720);
    public static readonly Quality WebDl1080p = new(3, "WEBDL-1080p", QualitySource.WebDl, 1080);
    public static readonly Quality WebDl2160p = new(18, "WEBDL-2160p", QualitySource.WebDl, 2160);

    public static readonly Quality WebRip480p = new(12, "WEBRip-480p", QualitySource.WebRip, 480);
    public static readonly Quality WebRip720p = new(14, "WEBRip-720p", QualitySource.WebRip, 720);
    public static readonly Quality WebRip1080p = new(15, "WEBRip-1080p", QualitySource.WebRip, 1080);
    public static readonly Quality WebRip2160p = new(17, "WEBRip-2160p", QualitySource.WebRip, 2160);

    public static readonly Quality Bluray480p = new(20, "Bluray-480p", QualitySource.Bluray, 480);
    public static readonly Quality Bluray576p = new(21, "Bluray-576p", QualitySource.Bluray, 576);
    public static readonly Quality Bluray720p = new(6, "Bluray-720p", QualitySource.Bluray, 720);
    public static readonly Quality Bluray1080p = new(7, "Bluray-1080p", QualitySource.Bluray, 1080);
    public static readonly Quality Bluray2160p = new(19, "Bluray-2160p", QualitySource.Bluray, 2160);
    public static readonly Quality Remux1080p = new(30, "Remux-1080p", QualitySource.Bluray, 1080, Modifier.Remux);
    public static readonly Quality Remux2160p = new(31, "Remux-2160p", QualitySource.Bluray, 2160, Modifier.Remux);
    public static readonly Quality BrDisk = new(22, "BR-DISK", QualitySource.Bluray, 1080, Modifier.BrDisk);

    public static readonly IReadOnlyList<Quality> All =
    [
        Unknown, Workprint, Cam, Telesync, Telecine, DvdScr, Regional,
        Sdtv, Dvd, DvdR,
        Hdtv720p, Hdtv1080p, Hdtv2160p, RawHd,
        WebDl480p, WebDl720p, WebDl1080p, WebDl2160p,
        WebRip480p, WebRip720p, WebRip1080p, WebRip2160p,
        Bluray480p, Bluray576p, Bluray720p, Bluray1080p, Bluray2160p, Remux1080p, Remux2160p, BrDisk,
    ];

    private static readonly FrozenDictionary<int, Quality> ById = All.ToFrozenDictionary(q => q.Id);

    public static Quality FindById(int id) =>
        ById.TryGetValue(id, out Quality? quality)
            ? quality
            : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown quality id.");

    // Identity is the id: a telesync carries its detected resolution but is still TELESYNC.
    public bool Equals(Quality? other) => other is not null && other.Id == Id;

    public override int GetHashCode() => Id;

    public override string ToString() => Name;
}
