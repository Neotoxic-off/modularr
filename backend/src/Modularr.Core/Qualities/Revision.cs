namespace Modularr.Core.Qualities;

public readonly record struct Revision(int Version = 1, int Real = 0, bool IsRepack = false) : IComparable<Revision>
{
    public Revision() : this(1)
    {
    }

    // A repack and a proper of the same version are the same revision.
    public bool Equals(Revision other) => Version == other.Version && Real == other.Real;

    public override int GetHashCode() => HashCode.Combine(Version, Real);

    public int CompareTo(Revision other) => (Real, Version).CompareTo((other.Real, other.Version));

    public static bool operator <(Revision left, Revision right) => left.CompareTo(right) < 0;

    public static bool operator >(Revision left, Revision right) => left.CompareTo(right) > 0;

    public static bool operator <=(Revision left, Revision right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Revision left, Revision right) => left.CompareTo(right) >= 0;

    public override string ToString() => Real > 0 ? $"v{Version} Real:{Real}" : $"v{Version}";
}
