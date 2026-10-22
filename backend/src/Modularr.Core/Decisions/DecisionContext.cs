using Modularr.Core.Qualities;

namespace Modularr.Core.Decisions;

// Everything a decision needs besides the release itself.
public sealed record DecisionContext
{
    public DecisionOptions Options { get; init; } = new();

    // Blocklist of the series or movie the releases belong to.
    public IReadOnlyCollection<BlocklistEntry> Blocklist { get; init; } = [];

    // Null uses the Sonarr or Radarr defaults for the candidate's media type.
    public IReadOnlyDictionary<Quality, QualityDefinition>? QualityDefinitions { get; init; }

    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;

    public QualityDefinition? GetDefinition(MediaType mediaType, Quality quality) =>
        (QualityDefinitions ?? QualityDefinition.DefaultsFor(mediaType)).GetValueOrDefault(quality);
}
