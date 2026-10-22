using System.Collections.Frozen;

namespace Modularr.Core.PostProcessing;

// Magic bytes of video containers: an .exe or a .zip renamed .mkv is not a video.
public static class VideoSignature
{
    // ISO 9660 and UDF volume descriptors sit at 32769.
    public const int HeaderLength = 0x8006;

    // Any container is accepted for any verified extension: mislabelled releases (an mkv named .mp4) are common and harmless.
    // shortcut: playlists, raw streams and rare containers (.strm, .m3u, .bin, .img, .nrg, .ty ...) are not verified, add their signatures if fakes show up there.
    private static readonly FrozenSet<string> VerifiedExtensions = new[]
    {
        ".mkv", ".mk3d", ".webm", ".mp4", ".m4v", ".mov", ".qt", ".3gp", ".avi", ".divx", ".xvid", ".ts", ".m2ts", ".mpg", ".mpeg",
        ".vob", ".m2v", ".wmv", ".asf", ".dvr-ms", ".flv", ".ogm", ".ogv", ".rm", ".rmvb", ".wtv", ".ifo", ".iso",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // Each container: every (offset, magic) part must match.
    private static readonly (int Offset, byte[] Magic)[][] Containers =
    [
        [(0, [0x1A, 0x45, 0xDF, 0xA3])],                         // Matroska, WebM
        [(4, "ftyp"u8.ToArray())],                                 // MP4, MOV, M4V, 3GP
        [(4, "moov"u8.ToArray())],                                 // QuickTime without ftyp
        [(4, "mdat"u8.ToArray())],
        [(4, "wide"u8.ToArray())],
        [(4, "free"u8.ToArray())],
        [(4, "skip"u8.ToArray())],
        [(0, "RIFF"u8.ToArray()), (8, "AVI "u8.ToArray())],        // AVI, DivX, XviD
        [(0, [0x47]), (188, [0x47])],                              // MPEG-TS
        [(4, [0x47]), (196, [0x47])],                              // M2TS: 4-byte timestamp before each packet
        [(0, [0x00, 0x00, 0x01, 0xBA])],                           // MPEG-PS, VOB
        [(0, [0x00, 0x00, 0x01, 0xB3])],                           // MPEG video elementary stream
        [(0, [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11])],   // ASF, WMV, DVR-MS
        [(0, "FLV"u8.ToArray())],
        [(0, "OggS"u8.ToArray())],                                 // OGM, OGV
        [(0, ".RMF"u8.ToArray())],                                 // RealMedia
        [(0, [0xB7, 0xD8, 0x00, 0x20, 0x37, 0x49, 0xDA, 0x11])],   // WTV
        [(0, "DVDVIDEO-V"u8.ToArray())],                           // DVD IFO
        [(0x8001, "CD001"u8.ToArray())],                           // ISO 9660
        [(0x8001, "BEA01"u8.ToArray())],                           // UDF
    ];

    // header: the first HeaderLength bytes of the file, or all of it when shorter.
    public static bool IsVideoContainer(string extension, ReadOnlySpan<byte> header)
    {
        if (!VerifiedExtensions.Contains(extension))
        {
            return true;
        }

        foreach ((int Offset, byte[] Magic)[] container in Containers)
        {
            if (Matches(header, container))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsVideoFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] header = new byte[Math.Min(stream.Length, HeaderLength)];
        stream.ReadExactly(header);
        return IsVideoContainer(Path.GetExtension(path), header);
    }

    private static bool Matches(ReadOnlySpan<byte> header, (int Offset, byte[] Magic)[] container)
    {
        foreach ((int offset, byte[] magic) in container)
        {
            if (header.Length < offset + magic.Length || !header.Slice(offset, magic.Length).SequenceEqual(magic))
            {
                return false;
            }
        }

        return true;
    }
}
