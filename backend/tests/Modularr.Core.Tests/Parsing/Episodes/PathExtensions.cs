namespace Modularr.Core.Tests.Parsing.Episodes;

internal static class PathExtensions
{
    // Upstream fixtures use Windows paths; on Linux C:\a\b becomes /C/a/b.
    public static string AsOsAgnostic(this string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return path;
        }

        if (path.Length > 2 && path[1] == ':')
        {
            path = Path.DirectorySeparatorChar + path.Replace(":", "", StringComparison.Ordinal);
        }

        return path.Replace('\\', Path.DirectorySeparatorChar);
    }
}
