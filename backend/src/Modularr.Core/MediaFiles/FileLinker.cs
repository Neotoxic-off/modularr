using System.Runtime.InteropServices;

namespace Modularr.Core.MediaFiles;

// Sonarr/Radarr "use hardlinks instead of copy": the library file shares the torrent's data, so seeding goes on at no disk cost.
public static partial class FileLinker
{
    // Hardlinks, or copies when the two paths are on different volumes (or the platform refuses); never overwrites.
    public static void LinkOrCopy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        bool linked = OperatingSystem.IsWindows() ? CreateHardLink(destination, source, IntPtr.Zero) : Link(source, destination) == 0;
        if (!linked)
        {
            File.Copy(source, destination, overwrite: false);
        }
    }

    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Link(string existingPath, string newPath);

    [LibraryImport("kernel32", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string newPath, string existingPath, IntPtr securityAttributes);
}
