namespace Modularr.Core.Decisions;

// Upstream DownloadDecisionMaker + DownloadDecisionPriorizationService, minus parsing and library mapping (done by the caller).
public static class DownloadDecisionMaker
{
    public static DownloadDecision GetDecision(ReleaseCandidate candidate, DecisionContext context) =>
        new(candidate, [.. DownloadSpecifications.All.Select(spec => spec(candidate, context)).OfType<Rejection>()]);

    // Best first; the decisions must belong to the same series or movie. Ties keep their input order.
    public static IReadOnlyList<DownloadDecision> Prioritize(IEnumerable<DownloadDecision> decisions, DecisionContext context) =>
        [.. decisions.OrderByDescending(d => d, new DownloadDecisionComparer(context))];
}
