// Ported from Sonarr/Radarr Profiles/Qualities/QualityIndexCompareToFixture and Qualities/QualityModelComparerFixture (identical in both).
using Modularr.Core.Decisions;
using Modularr.Core.Qualities;
using static Modularr.Core.Tests.Decisions.DecisionTestData;

namespace Modularr.Core.Tests.Decisions;

public sealed class QualityProfileTests
{
    public static TheoryData<MediaType> MediaTypes => [MediaType.Series, MediaType.Movie];

    [Theory]
    [InlineData(1, 0, 1, 0, 0)]
    [InlineData(1, 1, 1, 0, 1)]
    [InlineData(2, 0, 1, 0, 1)]
    [InlineData(1, 0, 1, 1, -1)]
    [InlineData(1, 0, 2, 0, -1)]
    public void QualityIndexRespectsGroupOrder(int leftIndex, int leftGroupIndex, int rightIndex, int rightGroupIndex, int expected)
    {
        Assert.Equal(expected, new QualityIndex(leftIndex, leftGroupIndex).CompareTo(new QualityIndex(rightIndex, rightGroupIndex), respectGroupOrder: true));
    }

    [Theory]
    [InlineData(1, 0, 1, 0, 0)]
    [InlineData(1, 1, 1, 0, 0)]
    [InlineData(2, 0, 1, 0, 1)]
    [InlineData(1, 0, 1, 1, 0)]
    [InlineData(1, 0, 2, 0, -1)]
    public void QualityIndexIgnoresGroupOrder(int leftIndex, int leftGroupIndex, int rightIndex, int rightGroupIndex, int expected)
    {
        Assert.Equal(expected, new QualityIndex(leftIndex, leftGroupIndex).CompareTo(new QualityIndex(rightIndex, rightGroupIndex), respectGroupOrder: false));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void GreaterWhenFirstQualityIsGreater(MediaType mediaType)
    {
        Assert.True(Profile(mediaType).Compare(Q(Quality.Bluray1080p), Q(Quality.Dvd)) > 0);
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void LesserWhenSecondQualityIsGreater(MediaType mediaType)
    {
        Assert.True(Profile(mediaType).Compare(Q(Quality.Dvd), Q(Quality.Bluray1080p)) < 0);
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void GreaterWhenFirstIsAProperOfTheSameQuality(MediaType mediaType)
    {
        Assert.True(Profile(mediaType).Compare(Q(Quality.Bluray1080p, version: 2), Q(Quality.Bluray1080p, version: 1)) > 0);
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void UsesTheProfileOrder(MediaType mediaType)
    {
        QualityProfile profile = new() { Items = DefaultQualities(mediaType, Quality.Bluray720p, Quality.Dvd) };

        Assert.True(profile.Compare(Q(Quality.Dvd), Q(Quality.Bluray720p)) > 0);
    }

    [Fact]
    public void IgnoresGroupOrderByDefault()
    {
        Assert.Equal(0, GroupedProfile().Compare(Q(Quality.Hdtv720p), Q(Quality.WebDl720p)));
    }

    [Fact]
    public void RespectsGroupOrderWhenAsked()
    {
        Assert.True(GroupedProfile().Compare(Q(Quality.Hdtv720p), Q(Quality.WebDl720p), respectGroupOrder: true) < 0);
    }

    [Fact]
    public void GroupIdCanBeTheCutoff()
    {
        QualityProfile profile = GroupedProfile() with { Cutoff = 1001 };

        Assert.Equal(new QualityIndex(2), profile.GetIndex(1001));
        Assert.Equal(Quality.WebDl720p, profile.GetItem(Quality.WebDl720p).Quality);
        Assert.Equal(Quality.Hdtv720p, profile.FirstAllowedQuality());
    }

    private static QualityProfile GroupedProfile() => new()
    {
        Items =
        [
            new QualityProfileQualityItem { Allowed = false, Quality = Quality.Sdtv },
            new QualityProfileQualityItem { Allowed = false, Quality = Quality.Dvd },
            new QualityProfileQualityItem
            {
                Id = 1001,
                Name = "720p",
                Allowed = true,
                Items =
                [
                    new QualityProfileQualityItem { Allowed = true, Quality = Quality.Hdtv720p },
                    new QualityProfileQualityItem { Allowed = true, Quality = Quality.WebDl720p },
                ],
            },
            new QualityProfileQualityItem { Allowed = true, Quality = Quality.Bluray720p },
        ],
    };
}
