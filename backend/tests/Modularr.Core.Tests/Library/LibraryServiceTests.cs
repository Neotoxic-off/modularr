using Modularr.Core.Configuration;
using Modularr.Core.Languages;
using Modularr.Core.Library;
using Modularr.Core.Metadata;
using Modularr.Core.Persistence;

namespace Modularr.Core.Tests.Library;

public sealed class LibraryServiceTests
{
    private static readonly PathOptions Paths = new() { Series = "/tv", Movies = "/movies", Anime = "/anime" };

    [Fact]
    public void AnimeSeriesKeepsItsEpisodesAndLeavesSpecialsUnmonitored()
    {
        SeriesInfo info = new()
        {
            TvdbId = 76885,
            Title = "Cowboy Bebop",
            OriginalLanguage = Language.Japanese,
            FirstAired = new DateOnly(1998, 4, 3),
            Runtime = 25,
            Genres = ["Animation", "Anime"],
            Overview = "Bounty hunters in space.",
            Images = [new MetadataImage(MediaCoverType.Fanart, "https://img/fanart.jpg"), new MetadataImage(MediaCoverType.Poster, "https://img/poster.jpg")],
            Seasons = [new SeasonInfo(0, null, []), new SeasonInfo(1, null, [])],
            Episodes =
            [
                new EpisodeInfo { TvdbId = 1, SeasonNumber = 0, EpisodeNumber = 1 },
                new EpisodeInfo { TvdbId = 2, SeasonNumber = 1, EpisodeNumber = 1, AbsoluteEpisodeNumber = 1, AirDate = new DateOnly(1998, 4, 3), Title = "Asteroid Blues" },
            ],
        };

        Series series = LibraryService.ToSeries(info, 4, Paths, DateTime.UnixEpoch);

        Assert.Equal((SeriesType.Anime, 1998, 25, 4), (series.SeriesType, series.Year, series.Runtime, series.QualityProfileId));
        Assert.Equal(Path.Combine("/anime", "Cowboy Bebop (1998)"), series.Path);
        Assert.Equal(("https://img/poster.jpg", "Bounty hunters in space."), (series.Poster, series.Overview));
        Assert.Equal(("https://img/fanart.jpg", null), (series.Backdrop, series.Rating));
        Assert.Equal(["Animation", "Anime"], series.Genres);
        Assert.Equal([false, true], series.Seasons.Select(s => s.Monitored));
        Episode first = series.Episodes.Single(e => e.SeasonNumber == 1);
        Assert.Equal((1, "Asteroid Blues", true), (first.AbsoluteEpisodeNumber!.Value, first.Title, first.Monitored));
    }

    [Theory]
    [InlineData("Star Trek: Discovery", 2017, "Star Trek Discovery (2017)")]
    [InlineData("What If...?", 0, "What If")]
    public void FolderNameDropsInvalidCharacters(string title, int year, string expected) =>
        Assert.Equal(expected, LibraryService.FolderName(title, year));

    [Theory]
    [InlineData("/tv/Show (2020)", true)]
    [InlineData("/elsewhere/Show", false)] // outside the library
    [InlineData("/old-tv-root", false)] // a root the title was added under before the roots changed
    [InlineData("/tv", false)]
    [InlineData("/tv/", false)]
    [InlineData("/movies", false)]
    [InlineData("/", false)]
    public void NeverDeletesARootFolder(string path, bool expected) =>
        Assert.Equal(expected, LibraryService.IsDeletableFolder(path, Paths));
}
