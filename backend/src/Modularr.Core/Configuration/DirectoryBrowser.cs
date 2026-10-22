namespace Modularr.Core.Configuration;

public sealed record DirectoryEntry(string Name, string Path);

// Path and Parent are null for the list of roots.
public sealed record DirectoryListing(string? Path, string? Parent, IReadOnlyList<DirectoryEntry> Directories);

// The folder picker behind the path settings: directories only, hidden, system and unreadable ones skipped.
public static class DirectoryBrowser
{
    private static readonly EnumerationOptions Enumeration = new() { IgnoreInaccessible = true };

    public static DirectoryListing Browse(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return new DirectoryListing(null, null, Roots());
        }

        if (!System.IO.Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{path}' is not an absolute path.", nameof(path));
        }

        DirectoryInfo directory = new(path);
        if (!directory.Exists)
        {
            throw new ArgumentException($"'{path}' does not exist.", nameof(path));
        }

        return new DirectoryListing(
            directory.FullName,
            directory.Parent?.FullName,
            [.. directory.EnumerateDirectories("*", Enumeration).Select(d => new DirectoryEntry(d.Name, d.FullName)).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)]);
    }

    private static DirectoryEntry[] Roots() => OperatingSystem.IsWindows()
        ? [.. DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => new DirectoryEntry(d.Name, d.Name))]
        : [new DirectoryEntry("/", "/")];
}
