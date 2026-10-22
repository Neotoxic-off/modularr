using Modularr.Core.Decisions;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;

namespace Modularr.Core.Tests.Persistence;

public sealed class DefaultQualityProfilesTests
{
    [Fact]
    public void SeedsTheSixUpstreamProfiles()
    {
        using TestDatabase database = new();
        using ModularrDbContext db = database.NewContext();

        Assert.Equal(["Any", "SD", "HD-720p", "HD-1080p", "Ultra-HD", "HD - 720p/1080p"], db.QualityProfiles.OrderBy(p => p.Id).Select(p => p.Name));
    }

    [Fact]
    public void EveryProfileRanksEveryQualityOnce()
    {
        foreach (QualityProfile profile in DefaultQualityProfiles.Create())
        {
            Assert.Equal(Quality.All.Select(q => q.Id).Order(), profile.Items.SelectMany(i => i.Quality is null ? i.Items : [i]).Select(i => i.Quality!.Id).Order());
        }
    }

    [Fact]
    public void EveryCutoffIsAllowed()
    {
        foreach (QualityProfile profile in DefaultQualityProfiles.Create())
        {
            Assert.True(profile.Items[profile.GetIndex(profile.Cutoff).Index].Allowed, profile.Name);
        }
    }

    // Radarr GetDefaultProfile groups WEB-DL and WEBRip of one resolution; a group is allowed when any member is.
    [Theory]
    [InlineData("SD", "WEB 480p", true)]
    [InlineData("SD", "WEB 720p", false)]
    [InlineData("HD-1080p", "WEB 1080p", true)]
    [InlineData("Ultra-HD", "WEB 2160p", true)]
    [InlineData("HD - 720p/1080p", "WEB 2160p", false)]
    public void WebSourcesAreGroupedPerResolution(string profileName, string group, bool allowed)
    {
        QualityProfile profile = DefaultQualityProfiles.Create().Single(p => p.Name == profileName);
        QualityProfileQualityItem item = profile.Items.Single(i => i.Name == group);

        Assert.Equal(2, item.Items.Count);
        Assert.All(item.Items, i => Assert.Equal(allowed, i.Allowed));
        Assert.Equal(allowed, item.Allowed);
    }

    [Fact]
    public void RemuxRanksAboveBlurayAndRawHdLast()
    {
        QualityProfile profile = DefaultQualityProfiles.Create()[0];
        int Rank(Quality quality) => profile.GetIndex(quality).Index;

        Assert.True(Rank(Quality.Remux1080p) > Rank(Quality.Bluray1080p));
        Assert.True(Rank(Quality.Hdtv2160p) > Rank(Quality.Remux1080p));
        Assert.Equal(profile.Items.Count - 1, Rank(Quality.RawHd));
    }
}
