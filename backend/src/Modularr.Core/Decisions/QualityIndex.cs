namespace Modularr.Core.Decisions;

// Position of a quality in a profile: the item, then the quality inside a group item.
public readonly record struct QualityIndex(int Index, int GroupIndex = 0)
{
    public int CompareTo(QualityIndex other, bool respectGroupOrder)
    {
        int indexCompare = Index.CompareTo(other.Index);

        return respectGroupOrder && indexCompare == 0 ? GroupIndex.CompareTo(other.GroupIndex) : indexCompare;
    }
}
