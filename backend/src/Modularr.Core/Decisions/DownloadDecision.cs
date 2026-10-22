namespace Modularr.Core.Decisions;

public sealed record DownloadDecision(ReleaseCandidate Candidate, IReadOnlyList<Rejection> Rejections)
{
    public bool Approved => Rejections.Count == 0;

    public bool TemporarilyRejected => Rejections.Count > 0 && Rejections.All(r => r.Type == RejectionType.Temporary);

    public bool Rejected => Rejections.Any(r => r.Type == RejectionType.Permanent);

    public override string ToString() => Approved ? $"[OK] {Candidate}" : $"[Rejected {Rejections.Count}] {Candidate}";
}
