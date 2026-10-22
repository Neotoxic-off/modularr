using System.Collections.Frozen;

namespace Modularr.Core.PostProcessing;

// Magic bytes of video containers: an .exe or a .zip renamed .mkv is not a video.
public static class VideoSignature
{
    // ISO 9660 and UDF volume descriptors sit at 32769.
    public const int HeaderLength = 0x8006;

    // Any container is accepted for any verified extension: mislabelled releases (an mkv named .mp4) are common and harmless.
    // shortcut: playlists, raw streams and rare containers (.strm, .m3u, .bin, .img, .nrg, .ty ...) are only checked against NonVideo, add their signatures if other fakes show up there.
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
        [(4, "styp"u8.ToArray())],                                 // CMAF segment
        [(4, "uuid"u8.ToArray())],
        [(4, "pnot"u8.ToArray())],                                 // QuickTime preview
        [(0, "RIFF"u8.ToArray()), (8, "AVI "u8.ToArray())],        // AVI, DivX, XviD
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

    // Executables and archives renamed to an unverified extension (Movie.2020.1080p.bin): the classic fake release.
    private static readonly byte[][] NonVideo =
    [
        "MZ"u8.ToArray(),                           // Windows executable
        [0x7F, (byte)'E', (byte)'L', (byte)'F'],    // Linux executable
        [0x50, 0x4B, 0x03, 0x04],                   // zip
        "Rar!"u8.ToArray(),
        [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C],       // 7z
    ];

    // header: the first HeaderLength bytes of the file, or all of it when shorter.
    public static bool IsVideoContainer(string extension, ReadOnlySpan<byte> header)
    {
        if (!VerifiedExtensions.Contains(extension))
        {
            foreach (byte[] magic in NonVideo)
            {
                if (header.StartsWith(magic))
                {
                    return false;
                }
            }

            return true;
        }

        foreach ((int Offset, byte[] Magic)[] container in Containers)
        {
            if (Matches(header, container))
            {
                return true;
            }
        }

        return IsTransportStream(header, packetLength: 188) || IsTransportStream(header, packetLength: 192);
    }

    public static bool IsVideoFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] header = new byte[Math.Min(stream.Length, HeaderLength)];
        stream.ReadExactly(header);
        return IsVideoContainer(Path.GetExtension(path), header);
    }

    // MPEG-TS (188-byte packets) or M2TS (a 4-byte timestamp before each): three sync bytes a packet apart.
    // A cut capture or stream dump can start mid-packet, so the first sync byte may sit anywhere in the first packet.
    private static bool IsTransportStream(ReadOnlySpan<byte> header, int packetLength)
    {
        const byte Sync = 0x47;

        for (int i = 0; i < packetLength && i + (2 * packetLength) < header.Length; i++)
        {
            if (header[i] == Sync && header[i + packetLength] == Sync && header[i + (2 * packetLength)] == Sync)
            {
                return true;
            }
        }

        return false;
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
