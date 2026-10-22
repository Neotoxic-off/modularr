namespace Modularr.Core.Qualities;

public sealed record QualityModel(Quality Quality, Revision Revision)
{
    public QualityDetectionSource SourceDetectionSource { get; init; }
    public QualityDetectionSource ResolutionDetectionSource { get; init; }
    public QualityDetectionSource ModifierDetectionSource { get; init; }
    public QualityDetectionSource RevisionDetectionSource { get; init; }

    // Detection sources describe how it was parsed, not what it is.
    public bool Equals(QualityModel? other) => other is not null && other.Quality == Quality && other.Revision == Revision;

    public override int GetHashCode() => HashCode.Combine(Quality, Revision);

    public override string ToString() => $"{Quality} {Revision}";
}
