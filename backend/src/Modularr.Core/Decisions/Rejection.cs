namespace Modularr.Core.Decisions;

public sealed record Rejection(DownloadRejectionReason Reason, string Message, RejectionType Type = RejectionType.Permanent)
{
    public override string ToString() => Type == RejectionType.Temporary ? $"[{Reason}] {Message} (temporary)" : $"[{Reason}] {Message}";
}
