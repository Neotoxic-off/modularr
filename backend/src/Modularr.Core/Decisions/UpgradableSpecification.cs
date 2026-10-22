using Modularr.Core.Qualities;

namespace Modularr.Core.Decisions;

// shortcut: custom formats are not ported (scores are always 0), port CustomFormat + profile format items when they land.
public static class UpgradableSpecification
{
    public static UpgradeableRejectReason IsUpgradable(QualityProfile profile, QualityModel currentQuality, QualityModel newQuality, ProperDownloadType propers)
    {
        int qualityCompare = profile.Compare(newQuality.Quality, currentQuality.Quality);

        if (qualityCompare > 0 && QualityCutoffNotMet(profile, currentQuality, newQuality))
        {
            return UpgradeableRejectReason.None;
        }

        if (qualityCompare < 0)
        {
            return UpgradeableRejectReason.BetterQuality;
        }

        int revisionCompare = newQuality.Revision.CompareTo(currentQuality.Revision);

        // Unless propers are not preferred, a better revision of the same quality is always an upgrade.
        if (propers != ProperDownloadType.DoNotPrefer && revisionCompare > 0)
        {
            return UpgradeableRejectReason.None;
        }

        if (!profile.UpgradeAllowed)
        {
            return UpgradeableRejectReason.UpgradesNotAllowed;
        }

        if (propers != ProperDownloadType.DoNotPrefer && revisionCompare < 0)
        {
            return UpgradeableRejectReason.BetterRevision;
        }

        return qualityCompare > 0 ? UpgradeableRejectReason.QualityCutoff : UpgradeableRejectReason.CustomFormatScore;
    }

    // Without custom formats this is also upstream CutoffNotMet.
    public static bool QualityCutoffNotMet(QualityProfile profile, QualityModel currentQuality, QualityModel? newQuality = null)
    {
        int cutoff = profile.UpgradeAllowed ? profile.Cutoff : profile.FirstAllowedQuality().Id;

        return profile.Compare(currentQuality.Quality.Id, cutoff) < 0
            || (newQuality is not null && IsRevisionUpgrade(currentQuality, newQuality));
    }

    // Same quality only: a proper of a WEBRip never upgrades a WEBDL and vice versa.
    public static bool IsRevisionUpgrade(QualityModel currentQuality, QualityModel newQuality) =>
        currentQuality.Quality == newQuality.Quality && newQuality.Revision.CompareTo(currentQuality.Revision) > 0;

    public static bool IsUpgradeAllowed(QualityProfile profile, QualityModel currentQuality, QualityModel newQuality) =>
        IsRevisionUpgrade(currentQuality, newQuality) || profile.UpgradeAllowed || profile.Compare(newQuality, currentQuality) <= 0;
}
