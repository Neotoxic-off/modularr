using System.IO.Compression;

namespace Modularr.Core.Tests.PostProcessing;

// A real completed-download folder on disk, removed after the test.
public sealed class TempDownload : IDisposable
{
    public string Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "modularr-pp-" + Guid.NewGuid().ToString("N"))).FullName;

    public string PathOf(string relative) => Path.Combine(Root, relative);

    public string CreateFile(string relative, int size = 16)
    {
        string path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    public string Zip(string relative, params (string Name, byte[] Content)[] entries)
    {
        string path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ZipBytes(entries));
        return path;
    }

    public string Fixture(string name)
    {
        string path = PathOf(name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "PostProcessing", "Fixtures", name), path);
        return path;
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
