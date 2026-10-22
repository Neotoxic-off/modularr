using Modularr.Core.Configuration;

namespace Modularr.Core.Library;

// The folders a scan or an import may read: inside a library root, never reached through a link.
public static class LibraryPaths
{
    public static StringComparison Comparison { get; } = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static IReadOnlyList<string> Roots(PathOptions paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return [Normalize(paths.Series), Normalize(paths.Movies), Normalize(paths.Anime)];
    }

    public static bool IsInside(string path, string root)
    {
        string prefix = Normalize(root);
        return Normalize(path).StartsWith(Path.EndsInDirectorySeparator(prefix) ? prefix : prefix + Path.DirectorySeparatorChar, Comparison);
    }

    // The same folder, or one inside the other.
    public static bool Overlaps(string a, string b) => string.Equals(Normalize(a), Normalize(b), Comparison) || IsInside(a, b) || IsInside(b, a);

    // The root the folder is inside, null when it is in none or a folder on the way down is a link, junction or other reparse point.
    public static string? ScannableRoot(string folder, PathOptions paths)
    {
        string? root = Roots(paths).FirstOrDefault(r => IsInside(folder, r));
        for (DirectoryInfo? directory = new(Normalize(folder)); root is not null && directory is not null && IsInside(directory.FullName, root); directory = directory.Parent)
        {
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return null;
            }
        }

        return root;
    }
}
