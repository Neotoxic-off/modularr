namespace Modularr.Core.Decisions;

// Upstream values minus the custom format cutoff/increment ones, unreachable without custom formats.
public enum UpgradeableRejectReason
{
    None,
    BetterQuality,
    BetterRevision,
    QualityCutoff,

    // Kept under its upstream name: without custom formats both scores are 0, so this means "not better".
    CustomFormatScore,
    UpgradesNotAllowed,
}
