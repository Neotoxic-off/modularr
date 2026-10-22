using System.Runtime.InteropServices;

namespace Modularr.Core.MediaFiles;

// Sonarr/Radarr "use hardlinks instead of copy": the library file shares the torrent's data, so seeding goes on at no disk cost.
public static partial class FileLinker
{
    // Hardlinks, or copies when the two paths are on different volumes (or the platform refuses); never overwrites.
    public static void LinkOrCopy(string source, string destination)
    {
        if (!TryLink(source, destination))
        {
            File.Copy(source, destination, overwrite: false);
        }
    }

    // A link that succeeds proves both paths are on one filesystem, where linking then unlinking the source is a rename. Across
    // filesystems it copies, checks the copy's size, then deletes the source. Any failure leaves the source as it was and removes
    // a partial copy; an existing destination is never touched.
    public static void Move(string source, string destination)
    {
        if (File.Exists(destination))
        {
            throw new IOException($"'{destination}' already exists.");
        }

        bool linked = TryLink(source, destination);
        try
        {
            if (!linked)
            {
                File.Copy(source, destination, overwrite: false);
                if (new FileInfo(destination).Length != new FileInfo(source).Length)
                {
                    throw new IOException($"The copy of '{source}' to '{destination}' is incomplete.");
                }
            }

            File.Delete(source);
        }
        catch
        {
            File.Delete(destination);
            throw;
        }
    }

    private static bool TryLink(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        return OperatingSystem.IsWindows() ? CreateHardLink(destination, source, IntPtr.Zero) : Link(source, destination) == 0;
    }

    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Link(string existingPath, string newPath);

    [LibraryImport("kernel32", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string newPath, string existingPath, IntPtr securityAttributes);
}
