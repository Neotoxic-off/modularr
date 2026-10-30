using System.ComponentModel.DataAnnotations;

namespace Modularr.Core.Library;

// What keeps the library up to date on its own: the metadata refresh and the search for missing and upgradable titles.
public sealed record AutomationOptions
{
    // Hours between two metadata refreshes of every title, 0 turns it off.
    [Range(0, 720)]
    public int RefreshIntervalHours { get; init; } = 24;

    // Hours between two scheduled searches, 0 turns them off.
    [Range(0, 720)]
    public int SearchIntervalHours { get; init; } = 6;

    // Titles searched per scheduled search; the next search goes on from where this one stopped.
    [Range(1, 500)]
    public int SearchTitlesPerCycle { get; init; } = 20;
}
