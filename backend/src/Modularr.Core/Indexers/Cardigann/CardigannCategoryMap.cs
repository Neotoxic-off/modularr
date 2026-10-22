using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Modularr.Core.Indexers.Definitions;

namespace Modularr.Core.Indexers.Cardigann;

// Prowlarr IndexerCapabilitiesCategories, limited to what a Cardigann search needs: tracker <-> Torznab ids.
public sealed class CardigannCategoryMap
{
    private readonly List<Mapping> mappings = [];

    public CardigannCategoryMap(CapabilitiesBlock caps)
    {
        ArgumentNullException.ThrowIfNull(caps);

        // Unknown Torznab names are skipped, like upstream (which logs them).
        foreach ((string trackerId, string name) in caps.Categories ?? new Dictionary<string, string>())
        {
            if (NewznabCategories.IdByName(name) is int id)
            {
                mappings.Add(new Mapping(trackerId, null, id));
            }
        }

        List<string> defaults = [];
        foreach (CategoryMapping mapping in caps.CategoryMappings ?? [])
        {
            int? id = mapping.Cat is null ? null : NewznabCategories.IdByName(mapping.Cat);
            if (mapping.Cat is not null && id is null)
            {
                continue;
            }

            if (id is not null)
            {
                mappings.Add(new Mapping(mapping.Id, mapping.Desc, id.Value));
            }

            // A described tracker category also gets its own stable custom id (>= 100000), so 3rd-party apps can target it.
            if (mapping.Desc is not null)
            {
                mappings.Add(new Mapping(mapping.Id, mapping.Desc, CustomId(mapping.Id)));
            }

            if (mapping.Default)
            {
                defaults.Add(mapping.Id);
            }
        }

        DefaultTrackerCategories = defaults;
    }

    public IReadOnlyList<string> DefaultTrackerCategories { get; }

    // A standard parent id also selects every child category (upstream ExpandTorznabQueryCategories).
    public IReadOnlyList<string> MapQueryCategories(IReadOnlyList<int> queryCategories) =>
        [.. mappings
            .Where(mapping => queryCategories.Any(query => mapping.TorznabId == query || (query < 100000 && NewznabCategories.ParentOf(mapping.TorznabId) == query)))
            .Select(mapping => mapping.TrackerId)
            .Distinct()];

    public IEnumerable<int> MapTrackerCategory(string? trackerId) =>
        string.IsNullOrWhiteSpace(trackerId) ? [] : mappings.Where(mapping => string.Equals(mapping.TrackerId, trackerId, StringComparison.OrdinalIgnoreCase)).Select(mapping => mapping.TorznabId);

    public IEnumerable<int> MapTrackerCategoryDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? [] : mappings.Where(mapping => string.Equals(mapping.Description, description, StringComparison.OrdinalIgnoreCase)).Select(mapping => mapping.TorznabId);

    private static int CustomId(string trackerId)
    {
        if (!int.TryParse(trackerId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
        {
            // Not a number: a stable hash in 0..65535, as upstream.
#pragma warning disable CA5350 // Not security: the id must match the one Prowlarr derives from SHA1.
            id = BitConverter.ToUInt16(SHA1.HashData(Encoding.UTF8.GetBytes(trackerId)), 0);
#pragma warning restore CA5350
        }

        return id + 100000;
    }

    private sealed record Mapping(string TrackerId, string? Description, int TorznabId);
}
