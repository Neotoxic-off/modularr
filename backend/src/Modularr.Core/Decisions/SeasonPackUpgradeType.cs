namespace Modularr.Core.Decisions;

// How many episodes of a season pack must be upgrades (or missing) before it replaces existing files.
public enum SeasonPackUpgradeType
{
    All,
    Threshold,
    Any,
}
