using Modularr.Core.Qualities;

namespace Modularr.Core.Persistence;

public sealed class QualityProfile
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public bool UpgradeAllowed { get; set; }

    // A quality, or any quality of the group that holds it (upstream stores a group id instead).
    public required Quality Cutoff { get; set; }

    // Ordered worst to best, stored as JSON.
    public required IReadOnlyList<QualityProfileItem> Items { get; set; }
}

// One quality (Name null) or a named group of equally ranked qualities.
public sealed record QualityProfileItem(string? Name, IReadOnlyList<Quality> Qualities, bool Allowed);
