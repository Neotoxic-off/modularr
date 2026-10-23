using System.Buffers;
using System.Globalization;
using System.Text;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;

namespace Modularr.Core.MediaFiles;

// Sonarr/Radarr "rename episodes/movies" with the default formats: one clean base name, the extension added by the caller.
public static class FileNamer
{
    // Invalid on Windows (a superset of Linux and macOS), control characters included.
    private static readonly SearchValues<char> InvalidChars = SearchValues.Create("<>:\"/\\|?*" + new string([.. Enumerable.Range(0, 32).Select(code => (char)code)]));

    // UTF-8 bytes, under the 255 a file name may use, leaving room for the extension, a subtitle's language and a suffix.
    private const int MaxBytes = 200;

    // "Series - S01E01 - Title [Quality]", "S01E01-E02 - Title + Title" for a multi-episode file.
    public static string ForEpisodes(Series series, IReadOnlyList<Episode> episodes, Quality quality)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(episodes);
        List<Episode> ordered = [.. episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber)];
        string numbers = string.Create(CultureInfo.InvariantCulture, $"S{ordered[0].SeasonNumber:00}E{ordered[0].EpisodeNumber:00}");
        if (ordered.Count > 1)
        {
            numbers += string.Create(CultureInfo.InvariantCulture, $"-E{ordered[^1].EpisodeNumber:00}");
        }

        string titles = string.Join(" + ", ordered.Select(e => e.Title).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct());
        return Sanitize($"{series.Title} - {numbers}{(titles.Length > 0 ? $" - {titles}" : "")}{Tag(quality)}");
    }

    // "Movie (Year) [Quality]".
    public static string ForMovie(Movie movie, Quality quality)
    {
        ArgumentNullException.ThrowIfNull(movie);
        return Sanitize(movie.Year > 0 ? string.Create(CultureInfo.InvariantCulture, $"{movie.Title} ({movie.Year}){Tag(quality)}") : $"{movie.Title}{Tag(quality)}");
    }

    // Invalid characters dropped, length capped, no trailing dot or space (Windows strips them, so two names would collide).
    public static string Sanitize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        StringBuilder clean = new(name.Length);
        foreach (char c in name.Where(c => !InvalidChars.Contains(c)))
        {
            clean.Append(c);
        }

        while (Encoding.UTF8.GetByteCount(clean.ToString()) > MaxBytes)
        {
            clean.Length -= char.IsLowSurrogate(clean[^1]) ? 2 : 1;
        }

        return clean.ToString().Trim().TrimEnd('.', ' ');
    }

    // The path itself when free, else "name (2).ext", "name (3).ext"...
    public static string Unused(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string stem = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        string candidate = path;
        for (int n = 2; File.Exists(candidate) || Directory.Exists(candidate); n++)
        {
            candidate = string.Create(CultureInfo.InvariantCulture, $"{stem} ({n}){Path.GetExtension(path)}");
        }

        return candidate;
    }

    private static string Tag(Quality quality) => quality.Id == 0 ? "" : $" [{quality.Name}]";
}
