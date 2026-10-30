using Modularr.Core.Languages;
using Modularr.Core.Library;
using Modularr.Core.Metadata;
using Modularr.Core.Persistence;

namespace Modularr.Core.Tests.Library;

public sealed class MergeEpisodesTests
{
    [Fact]
    public void NewEpisodesFollowTheirSeasonExistingOnesAreUpdatedAndOnlyFilelessOnesGoneUpstreamAreRemoved()
    {
        Series series = new() { TvdbId = 1, Title = "S", SortTitle = "s", Path = "/tv/s", Monitored = true };
        series.Seasons.Add(new Season { Number = 1, Monitored = true });
        series.Seasons.Add(new Season { Number = 2, Monitored = false });
        series.Episodes.Add(new Episode { SeasonNumber = 1, EpisodeNumber = 1, Title = "Old", Monitored = true });
        series.Episodes.Add(new Episode { SeasonNumber = 1, EpisodeNumber = 8, MediaFileId = 5, Monitored = true });
        series.Episodes.Add(new Episode { SeasonNumber = 1, EpisodeNumber = 9, Monitored = true });
        SeriesInfo info = new()
        {
            TvdbId = 1,
            Title = "S",
            OriginalLanguage = Language.English,
            Seasons = [new SeasonInfo(0, null, []), new SeasonInfo(1, null, []), new SeasonInfo(2, null, []), new SeasonInfo(3, null, [])],
            Episodes =
            [
                Ep(1, 1, "Pilot", new DateOnly(2026, 1, 1)),
                Ep(1, 2, "Second", new DateOnly(2026, 1, 8)),
                Ep(2, 1, "Unmonitored season", null),
                Ep(3, 1, "New season", null),
                Ep(0, 1, "Special", null),
            ],
        };

        Assert.Equal(4, LibraryService.MergeEpisodes(series, info));

        Episode Find(int season, int episode) => series.Episodes.Single(e => e.SeasonNumber == season && e.EpisodeNumber == episode);
        Assert.Equal(("Pilot", new DateOnly(2026, 1, 1)), (Find(1, 1).Title, Find(1, 1).AirDate));
        Assert.Equal([true, false, true, false], new[] { Find(1, 2), Find(2, 1), Find(3, 1), Find(0, 1) }.Select(e => e.Monitored));
        Assert.Equal([(0, false), (1, true), (2, false), (3, true)], series.Seasons.OrderBy(s => s.Number).Select(s => (s.Number, s.Monitored)));
        Assert.Equal(5, Find(1, 8).MediaFileId);
        Assert.DoesNotContain(series.Episodes, e => e.EpisodeNumber == 9);
    }

    [Fact]
    public void AnUnmonitoredSeriesGetsItsNewEpisodesUnmonitored()
    {
        Series series = new() { TvdbId = 1, Title = "S", SortTitle = "s", Path = "/tv/s", Monitored = false };
        series.Seasons.Add(new Season { Number = 1, Monitored = true });
        SeriesInfo info = new() { TvdbId = 1, Title = "S", OriginalLanguage = Language.English, Seasons = [new SeasonInfo(1, null, [])], Episodes = [Ep(1, 1, "Pilot", null)] };

        Assert.Equal(1, LibraryService.MergeEpisodes(series, info));
        Assert.False(series.Episodes.Single().Monitored);
    }

    private static EpisodeInfo Ep(int season, int episode, string title, DateOnly? airDate) =>
        new() { TvdbId = (season * 100) + episode, SeasonNumber = season, EpisodeNumber = episode, Title = title, AirDate = airDate };
}
