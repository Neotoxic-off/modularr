using System.ComponentModel.DataAnnotations;

namespace Modularr.Core.Decisions;

// Global release decision settings (upstream config service values and the per-indexer minimum seeders).
public sealed record DecisionOptions
{
    public ProperDownloadType DownloadPropersAndRepacks { get; init; } = ProperDownloadType.PreferAndUpgrade;

    // Megabytes, 0 means unlimited.
    [Range(0, int.MaxValue)]
    public int MaximumSize { get; init; }

    // Minutes since publication, 0 means none.
    [Range(0, int.MaxValue)]
    public int MinimumAge { get; init; }

    [Range(0, int.MaxValue)]
    public int MinimumSeeders { get; init; } = 1;

    public SeasonPackUpgradeType SeasonPackUpgrade { get; init; } = SeasonPackUpgradeType.All;

    // Percent of upgradable episodes required in Threshold mode.
    [Range(0.0, 100.0)]
    public double SeasonPackUpgradeThreshold { get; init; } = 100.0;

    // Sonarr release profile flag: grab a season pack before every episode has aired.
    public bool AllowSeasonPackWithoutAllEpisodesAired { get; init; }
}
