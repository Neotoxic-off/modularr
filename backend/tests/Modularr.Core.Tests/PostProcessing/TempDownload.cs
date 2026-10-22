using System.IO.Compression;
using Modularr.Core.MediaFiles;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace Modularr.Core.Tests.PostProcessing;

// A real completed-download folder on disk, removed after the test.
public sealed class TempDownload : IDisposable
{
    // The smallest content the video check accepts: a Matroska header.
    public static byte[] Video { get; } = [0x1A, 0x45, 0xDF, 0xA3, .. new byte[12]];

    public string Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "modularr-pp-" + Guid.NewGuid().ToString("N"))).FullName;

    public string PathOf(string relative) => Path.Combine(Root, relative);

    // Video files start with a Matroska header so the content check accepts them.
    public string CreateFile(string relative, int size = 16)
    {
        byte[] content = new byte[size];
        if (MediaFileExtensions.VideoExtensions.Contains(Path.GetExtension(relative)))
        {
            Video.AsSpan(0, Math.Min(size, 4)).CopyTo(content);
        }

        return Write(relative, content);
    }

    public string Write(string relative, byte[] content)
    {
        string path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    public string Zip(string relative, params (string Name, byte[] Content)[] entries) => Write(relative, ZipBytes(entries));

    // Copies every fixture whose name starts with prefix: one file, or all volumes of a set.
    public void Fixture(string prefix)
    {
        foreach (string fixture in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "PostProcessing", "Fixtures"), prefix + "*"))
        {
            File.Copy(fixture, PathOf(Path.GetFileName(fixture)));
        }
    }

    public string[] Files() =>
        [.. Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/')).Order(StringComparer.Ordinal)];

    public static byte[] ZipBytes(params (string Name, byte[] Content)[] entries)
    {
        using MemoryStream stream = new();

        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in entries)
            {
                using Stream entry = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                entry.Write(content);
            }
        }

        return stream.ToArray();
    }

    public static byte[] TarBytes(params (string Name, byte[] Content)[] entries)
    {
        using MemoryStream stream = new();

        using (IWriter writer = WriterFactory.OpenWriter(stream, ArchiveType.Tar, new WriterOptions(CompressionType.None) { LeaveStreamOpen = true }))
        {
            foreach ((string name, byte[] content) in entries)
            {
                writer.Write(name, new MemoryStream(content));
            }
        }

        return stream.ToArray();
    }

    // compressor must leave the underlying stream open.
    public static byte[] Compress(byte[] content, Func<Stream, Stream> compressor)
    {
        using MemoryStream stream = new();

        using (Stream compressed = compressor(stream))
        {
            compressed.Write(content);
        }

        return stream.ToArray();
    }

    // System.IO.Compression cannot encrypt: set the "encrypted" flag bit of every local and central header instead.
    public static byte[] MarkEncrypted(byte[] zip)
    {
        for (int i = 0; i + 4 <= zip.Length; i++)
        {
            int signature = BitConverter.ToInt32(zip, i);
            int flagOffset = signature switch
            {
                0x04034b50 => 6,
                0x02014b50 => 8,
                _ => -1,
            };

            if (flagOffset > 0)
            {
                zip[i + flagOffset] |= 1;
            }
        }

        return zip;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
