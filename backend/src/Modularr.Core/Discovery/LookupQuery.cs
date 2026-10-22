using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Modularr.Core.Discovery;

// What the user typed in the add box: a normalized title with an optional trailing year, or a direct id ("tmdb:438631", "tvdb:81189", "imdb:tt1375666").
public sealed partial record LookupQuery(string Title, int? Year = null, string? IdSource = null, string? Id = null)
{
    public const int MinLength = 2;

    // Too short to be worth a metadata request.
    public bool IsEmpty => Id is null && Title.Length < MinLength;

    // The term handed to the metadata clients, which resolve "source:id" themselves.
    public string SearchTerm => Id is null ? Title : $"{IdSource}:{Id}";

    public static LookupQuery Parse(string term)
    {
        Match id = IdRegex.Match(term);
        if (id.Success)
        {
            string value = id.Groups["id"].Value.ToLowerInvariant();
            return new LookupQuery("", IdSource: id.Groups["source"].Success ? id.Groups["source"].Value.ToLowerInvariant() : "imdb", Id: value);
        }

        string title = Normalize(term);
        int space = title.LastIndexOf(' ');
        // A lone number is a title ("1917"), not a year.
        return space > 0
            && int.TryParse(title.AsSpan(space + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int year)
            && year is >= 1880 and <= 2100
            && title.Length - space == 5
            ? new LookupQuery(title[..space], year)
            : new LookupQuery(title);
    }

    // Lower case, no diacritics, apostrophes dropped ("Grey's" -> "greys"), every other non-alphanumeric run becomes one space.
    public static string Normalize(string text)
    {
        StringBuilder normalized = new(text.Length);
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c))
            {
                normalized.Append(char.ToLowerInvariant(c));
            }
            else if (c is not ('\'' or '’') && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark
                && normalized.Length > 0 && normalized[^1] != ' ')
            {
                normalized.Append(' ');
            }
        }

        return normalized.ToString().TrimEnd().Normalize(NormalizationForm.FormC);
    }

    // Merges per-source results (each in its source's relevance order) into one list: title match first, then the year, then the source order (half a point per place).
    public IReadOnlyList<DiscoveryItem> Rank(IEnumerable<IReadOnlyList<DiscoveryItem>> sources) =>
        [.. sources
            .SelectMany(items => items.Select((item, position) => (Item: item, Score: Score(item) - (position / 2.0))))
            .OrderByDescending(r => r.Score)
            .Select(r => r.Item)];

    private double Score(DiscoveryItem item)
    {
        string title = Normalize(item.Title);
        // The "year" was part of the title ("blade runner 2049"): the best possible match.
        if (Id is not null || (Year is not null && title == $"{Title} {Year}"))
        {
            return 125;
        }

        double score = title == Title ? 100
            : title.StartsWith(Title, StringComparison.Ordinal) ? 75
            : title.Contains(Title, StringComparison.Ordinal) ? 60
            : 50.0 * Title.Split(' ').Intersect(title.Split(' ')).Count() / Title.Split(' ').Length;

        return Year is null || item.Year is null ? score
            : item.Year == Year ? score + 25
            : score - 15;
    }

    [GeneratedRegex(@"^\s*(?:(?<source>tmdb|tvdb|imdb)(?:id)?\s*:\s*(?<id>tt\d+|\d+)|(?<id>tt\d{7,}))\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex IdRegex { get; }
}
