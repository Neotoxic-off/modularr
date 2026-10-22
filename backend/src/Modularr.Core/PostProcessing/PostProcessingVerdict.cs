namespace Modularr.Core.PostProcessing;

public abstract record PostProcessingVerdict
{
    private PostProcessingVerdict()
    {
    }

    public sealed record Accepted(IReadOnlyList<string> VideoFiles, IReadOnlyList<string> SubtitleFiles) : PostProcessingVerdict;

    // Blacklist is false only when the failure is not the release's fault.
    public sealed record Rejected(string Reason, bool Blacklist) : PostProcessingVerdict;
}
