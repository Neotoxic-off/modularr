using Modularr.Core.Qualities;

namespace Modularr.Core.Decisions;

// Either a single quality or a named group of qualities (Quality null, Items set) ranked as one.
public sealed record QualityProfileQualityItem
{
    // Group id, 0 for a single quality; a profile cutoff may point at it.
    public int Id { get; init; }
    public string? Name { get; init; }
    public Quality? Quality { get; init; }
    public IReadOnlyList<QualityProfileQualityItem> Items { get; init; } = [];
    public bool Allowed { get; init; }

    public override string ToString()
    {
        string qualities = Quality is null ? string.Join(", ", Items.Select(i => i.Quality)) : Quality.Name;

        return string.IsNullOrWhiteSpace(Name) ? qualities : $"{Name} ({qualities})";
    }
}
