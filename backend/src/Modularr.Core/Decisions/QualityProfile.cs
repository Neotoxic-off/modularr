using Modularr.Core.Qualities;

namespace Modularr.Core.Decisions;

// Sonarr/Radarr QualityProfile without custom formats; items are ordered worst to best. Also the persisted entity (Items is a JSON column)
// and the API shape. No language: MediaOptions is the one language policy.
public sealed record QualityProfile
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public bool UpgradeAllowed { get; init; }

    // Quality id or group id.
    public int Cutoff { get; init; }
    public IReadOnlyList<QualityProfileQualityItem> Items { get; init; } = [];

    public Quality FirstAllowedQuality()
    {
        QualityProfileQualityItem firstAllowed = Items.First(q => q.Allowed);

        // A group's first quality is the true first quality.
        return firstAllowed.Quality ?? firstAllowed.Items[0].Quality!;
    }

    public QualityIndex GetIndex(Quality quality, bool respectGroupOrder = false) => GetIndex(quality.Id, respectGroupOrder);

    // Upstream quirk kept: an id absent from the profile maps to the first item.
    public QualityIndex GetIndex(int id, bool respectGroupOrder = false)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            QualityProfileQualityItem item = Items[i];

            if (item.Quality?.Id == id || (item.Id > 0 && item.Id == id))
            {
                return new QualityIndex(i);
            }

            for (int g = 0; g < item.Items.Count; g++)
            {
                if (item.Items[g].Quality?.Id == id)
                {
                    return respectGroupOrder ? new QualityIndex(i, g) : new QualityIndex(i);
                }
            }
        }

        return new QualityIndex(0);
    }

    // The item holding the quality: the quality itself inside a group.
    public QualityProfileQualityItem GetItem(Quality quality)
    {
        QualityIndex index = GetIndex(quality, respectGroupOrder: true);
        QualityProfileQualityItem qualityOrGroup = Items[index.Index];

        return qualityOrGroup.Quality is null ? qualityOrGroup.Items[index.GroupIndex] : qualityOrGroup;
    }

    // Upstream QualityModelComparer.
    public int Compare(int leftId, int rightId) => GetIndex(leftId).CompareTo(GetIndex(rightId), respectGroupOrder: false);

    public int Compare(Quality left, Quality right, bool respectGroupOrder = false) =>
        GetIndex(left, respectGroupOrder).CompareTo(GetIndex(right, respectGroupOrder), respectGroupOrder);

    public int Compare(QualityModel left, QualityModel right, bool respectGroupOrder = false)
    {
        int result = Compare(left.Quality, right.Quality, respectGroupOrder);

        return result == 0 ? left.Revision.CompareTo(right.Revision) : result;
    }
}
