// Ported from Sonarr/Radarr DecisionEngineTests UpgradeSpecificationFixture and UpgradeAllowedSpecificationFixture,
// run against the Sonarr (series) and Radarr (movie) default quality lists. Custom format cases are skipped (not ported).
using Modularr.Core.Decisions;
using Modularr.Core.Qualities;
using static Modularr.Core.Tests.Decisions.DecisionTestData;

namespace Modularr.Core.Tests.Decisions;

public sealed class UpgradableSpecificationTests
{
    public static TheoryData<MediaType, Quality, int, Quality, int, UpgradeableRejectReason> IsUpgradeCases()
    {
        TheoryData<MediaType, Quality, int, Quality, int, UpgradeableRejectReason> data = [];

        foreach (MediaType mediaType in new[] { MediaType.Series, MediaType.Movie })
        {
            data.Add(mediaType, Quality.Sdtv, 1, Quality.Sdtv, 2, UpgradeableRejectReason.None);
            data.Add(mediaType, Quality.WebDl720p, 1, Quality.WebDl720p, 2, UpgradeableRejectReason.None);
            data.Add(mediaType, Quality.Sdtv, 1, Quality.Sdtv, 1, UpgradeableRejectReason.CustomFormatScore);
            data.Add(mediaType, Quality.WebDl720p, 1, Quality.Hdtv720p, 2, UpgradeableRejectReason.BetterQuality);
            data.Add(mediaType, Quality.WebDl720p, 1, Quality.WebDl720p, 1, UpgradeableRejectReason.CustomFormatScore);
            data.Add(mediaType, Quality.WebDl1080p, 1, Quality.WebDl1080p, 1, UpgradeableRejectReason.CustomFormatScore);
        }

        return data;
    }

    public static TheoryData<MediaType> MediaTypes => [MediaType.Series, MediaType.Movie];

    [Theory]
    [MemberData(nameof(IsUpgradeCases))]
    public void IsUpgrade(MediaType mediaType, Quality current, int currentVersion, Quality newQuality, int newVersion, UpgradeableRejectReason expected)
    {
        QualityProfile profile = Profile(mediaType, upgradeAllowed: true);

        Assert.Equal(expected, UpgradableSpecification.IsUpgradable(profile, Q(current, currentVersion), Q(newQuality, newVersion), ProperDownloadType.PreferAndUpgrade));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void ProperIsUpgradeWhenPropersAreNotUpgraded(MediaType mediaType)
    {
        Assert.Equal(UpgradeableRejectReason.None, UpgradableSpecification.IsUpgradable(Profile(mediaType), Q(Quality.Dvd, 1), Q(Quality.Dvd, 2), ProperDownloadType.DoNotUpgrade));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void ProperIsNotUpgradeWhenPropersAreNotPreferred(MediaType mediaType)
    {
        Assert.Equal(UpgradeableRejectReason.UpgradesNotAllowed, UpgradableSpecification.IsUpgradable(Profile(mediaType), Q(Quality.Dvd, 1), Q(Quality.Dvd, 2), ProperDownloadType.DoNotPrefer));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void SameReleaseAndExistingFileIsNotUpgrade(MediaType mediaType)
    {
        Assert.Equal(UpgradeableRejectReason.UpgradesNotAllowed, UpgradableSpecification.IsUpgradable(Profile(mediaType), Q(Quality.Hdtv720p), Q(Quality.Hdtv720p), ProperDownloadType.PreferAndUpgrade));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void HigherQualityIsUpgradeWhenCutoffIsNotMet(MediaType mediaType)
    {
        QualityProfile profile = Profile(mediaType, Quality.Hdtv1080p.Id, upgradeAllowed: true);

        Assert.Equal(UpgradeableRejectReason.None, UpgradableSpecification.IsUpgradable(profile, Q(Quality.Hdtv720p), Q(Quality.Hdtv1080p), ProperDownloadType.PreferAndUpgrade));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void HigherQualityIsNotUpgradeWhenCutoffIsMet(MediaType mediaType)
    {
        QualityProfile profile = Profile(mediaType, Quality.Hdtv720p.Id, upgradeAllowed: true);

        Assert.Equal(UpgradeableRejectReason.QualityCutoff, UpgradableSpecification.IsUpgradable(profile, Q(Quality.Hdtv720p), Q(Quality.Hdtv1080p), ProperDownloadType.PreferAndUpgrade));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void BetterQualityIsNotAllowedWhenUpgradingIsDisabled(MediaType mediaType)
    {
        Assert.False(UpgradableSpecification.IsUpgradeAllowed(UpgradeAllowedProfile(mediaType, false), Q(Quality.Dvd), Q(Quality.Bluray1080p)));
    }

    // Quality ids: 7 Bluray-1080p, 2 DVD, 1 SDTV (better, same, lower than the DVD on disk).
    [Theory]
    [InlineData(MediaType.Series, true, 7)]
    [InlineData(MediaType.Series, true, 2)]
    [InlineData(MediaType.Series, false, 2)]
    [InlineData(MediaType.Series, true, 1)]
    [InlineData(MediaType.Series, false, 1)]
    [InlineData(MediaType.Movie, true, 7)]
    [InlineData(MediaType.Movie, true, 2)]
    [InlineData(MediaType.Movie, false, 2)]
    [InlineData(MediaType.Movie, true, 1)]
    [InlineData(MediaType.Movie, false, 1)]
    public void UpgradeIsAllowed(MediaType mediaType, bool upgradeAllowed, int newQualityId)
    {
        Assert.True(UpgradableSpecification.IsUpgradeAllowed(UpgradeAllowedProfile(mediaType, upgradeAllowed), Q(Quality.Dvd), Q(Quality.FindById(newQualityId))));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void RevisionUpgradeIsAllowedWhenUpgradingIsDisabled(MediaType mediaType)
    {
        Assert.True(UpgradableSpecification.IsUpgradeAllowed(UpgradeAllowedProfile(mediaType, false), Q(Quality.Dvd, 1), Q(Quality.Dvd, 2)));
    }

    private static QualityProfile UpgradeAllowedProfile(MediaType mediaType, bool upgradeAllowed) =>
        Profile(mediaType, Quality.Bluray1080p.Id, upgradeAllowed);
}
