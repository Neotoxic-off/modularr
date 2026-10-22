// Behavior from Prowlarr IndexerCapabilitiesCategories (no upstream fixture): Torznab query ids -> tracker ids and back.
using Modularr.Core.Indexers.Cardigann;
using static Modularr.Core.Tests.Indexers.Cardigann.CardigannFixtures;

namespace Modularr.Core.Tests.Indexers.Cardigann;

public sealed class CardigannCategoryMapTests
{
    private readonly CardigannCategoryMap _map = new(Definition("1337x.yml").Caps);

    [Fact]
    public void ParentQueryCategorySelectsEveryChildTrackerCategory()
    {
        IReadOnlyList<string> tracker = _map.MapQueryCategories([5000]);

        Assert.Contains("41", tracker);
        Assert.Contains("71", tracker);
        Assert.DoesNotContain("42", tracker);
    }

    [Fact]
    public void ChildOrCustomQueryCategorySelectsOnlyItself()
    {
        Assert.Equal(["42"], _map.MapQueryCategories([2040]).Where(id => id == "42"));
        Assert.Equal(["42"], _map.MapQueryCategories([100042]));
        Assert.DoesNotContain("41", _map.MapQueryCategories([2040]));
    }

    [Fact]
    public void TrackerCategoryMapsToStandardAndCustomIds()
    {
        Assert.Equal([5040, 100041], _map.MapTrackerCategory("41"));
        Assert.Equal([5040, 100041], _map.MapTrackerCategoryDescription("tv/hd"));
        Assert.Empty(_map.MapTrackerCategory("does-not-exist"));
    }

    [Fact]
    public void NonNumericTrackerIdGetsAStableHashedCustomId()
    {
        CardigannCategoryMap nyaa = new(Definition("nyaasi.yml").Caps);

        // 1_2 is mapped twice (TV/Anime and Movies/Other), both with the same custom id.
        int custom = Assert.Single(nyaa.MapTrackerCategory("1_2").Where(id => id >= 100000).Distinct());
        Assert.InRange(custom, 100000, 165535);
        Assert.Contains(custom, new CardigannCategoryMap(Definition("nyaasi.yml").Caps).MapTrackerCategory("1_2"));
        Assert.DoesNotContain(custom, nyaa.MapTrackerCategory("1_3"));
    }
}
