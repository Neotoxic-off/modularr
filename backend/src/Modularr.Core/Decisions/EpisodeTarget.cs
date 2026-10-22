namespace Modularr.Core.Decisions;

// A library episode a release was mapped to.
public sealed record EpisodeTarget
{
    public int SeasonNumber { get; init; }
    public int EpisodeNumber { get; init; }

    // Minutes, 0 when unknown (the series runtime is used instead).
    public int Runtime { get; init; }
    public DateTimeOffset? AirDate { get; init; }
    public ExistingFile? File { get; init; }
}
