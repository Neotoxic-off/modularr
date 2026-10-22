using System.Text.RegularExpressions;
using Modularr.Core.Indexers.Cardigann;
using Modularr.Core.Indexers.Definitions;

namespace Modularr.Core.Indexers.Catalog;

// Every cached definition that can return movies or TV; modularr is video only so the rest (and adult sites) is left out.
public sealed partial record IndexerCatalog
{
    public static IndexerCatalog Empty { get; } = new() { Entries = [], Errors = [] };

    // Sorted by name.
    public required IReadOnlyList<IndexerCatalogEntry> Entries { get; init; }

    // Definition files that could not be loaded; the rest of the catalog still is.
    public required IReadOnlyList<DefinitionLoadError> Errors { get; init; }

    public IndexerCatalogEntry? Find(string id) => Entries.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));

    // A missing directory (first run offline) is an empty catalog.
    public static IndexerCatalog Load(IndexerOptions options)
    {
        string directory = options.DefinitionsDirectory;
        if (!Directory.Exists(directory))
        {
            return Empty;
        }

        HashSet<string> disabled = new(options.Disabled, StringComparer.OrdinalIgnoreCase);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<IndexerCatalogEntry> entries = [];
        List<DefinitionLoadError> errors = [];

        foreach (string file in Directory.EnumerateFiles(directory, "*.yml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            CardigannDefinition definition;
            try
            {
                definition = CardigannDefinitionLoader.Load(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                errors.Add(new DefinitionLoadError(Path.GetRelativePath(directory, file), ex.Message));
                continue;
            }

            // Prowlarr skips a definition whose id is not unique.
            if (!seen.Add(definition.Id))
            {
                errors.Add(new DefinitionLoadError(Path.GetRelativePath(directory, file), $"duplicate indexer id '{definition.Id}'."));
                continue;
            }

            IReadOnlyList<string> categories = Categories(definition);
            if (categories.Any(IsVideoCategory) && !IsAdult(definition))
            {
                // shortcut: private, semi-private and login-only indexers stay disabled until credentials can be configured.
                bool enabled = CardigannSearcher.IsSupported(definition) && !disabled.Contains(definition.Id);
                entries.Add(new IndexerCatalogEntry(definition, categories, enabled));
            }
        }

        return new IndexerCatalog
        {
            Entries = [.. entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)],
            Errors = errors,
        };
    }

    // Newznab category names the indexer maps to, from either caps.categories or caps.categorymappings.
    private static IReadOnlyList<string> Categories(CardigannDefinition definition) =>
    [
        .. (definition.Caps.Categories?.Values ?? [])
            .Concat(definition.Caps.CategoryMappings?.Select(mapping => mapping.Cat).OfType<string>() ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];

    // Newznab 2000 (Movies) and 5000 (TV) ranges, e.g. "Movies/UHD", "TV/Anime".
    private static bool IsVideoCategory(string category) => IsCategoryOf(category, "Movies") || IsCategoryOf(category, "TV");

    // "TV/Anime" is under "TV"; "TVShows" is not.
    public static bool IsCategoryOf(string category, string root)
    {
        ArgumentNullException.ThrowIfNull(category);
        return category.Equals(root, StringComparison.OrdinalIgnoreCase) || category.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }

    // Adult sites that only declare a video category (e-hentai maps everything to TV/Anime).
    private static bool IsAdult(CardigannDefinition definition) => AdultPattern().IsMatch(definition.Name + " " + definition.Description);

    [GeneratedRegex(@"\b(hentai|porn|xxx)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AdultPattern();
}

public sealed record IndexerCatalogEntry(CardigannDefinition Definition, IReadOnlyList<string> Categories, bool Enabled)
{
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Description => Definition.Description;
    public string Language => Definition.Language;
    public IndexerPrivacy Privacy => Definition.Privacy;
    public IReadOnlyList<string> Links => Definition.Links;
}

public sealed record DefinitionLoadError(string File, string Error);
