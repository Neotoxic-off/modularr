using Modularr.Core.Decisions;
using Modularr.Core.Discovery;

namespace Modularr.Core.Tests.Discovery;

public sealed class LookupQueryTests
{
    [Theory]
    [InlineData("Amélie", "amelie", null)]
    [InlineData("  The  OFFICE!! ", "the office", null)]
    [InlineData("Grey's Anatomy", "greys anatomy", null)]
    [InlineData("Spider-Man: No Way Home", "spider man no way home", null)]
    [InlineData("dune 2021", "dune", 2021)]
    [InlineData("Dune (2021)", "dune", 2021)]
    [InlineData("1917", "1917", null)]
    [InlineData("blade runner 2049", "blade runner", 2049)]
    [InlineData("ocean's 11", "oceans 11", null)]
    [InlineData("hitchcock 1850", "hitchcock 1850", null)]
    public void NormalizesTitleAndTrailingYear(string term, string title, int? year)
    {
        LookupQuery query = LookupQuery.Parse(term);

        Assert.Equal((title, year, null), (query.Title, query.Year, query.Id));
    }

    [Theory]
    [InlineData("tmdb:438631", "tmdb", "438631")]
    [InlineData("TVDB: 81189", "tvdb", "81189")]
    [InlineData("imdb:tt1375666", "imdb", "tt1375666")]
    [InlineData("tmdbid:603", "tmdb", "603")]
    [InlineData(" tt1375666 ", "imdb", "tt1375666")]
    public void RecognizesDirectIds(string term, string source, string id)
    {
        LookupQuery query = LookupQuery.Parse(term);

        Assert.Equal((source, id, $"{source}:{id}"), (query.IdSource, query.Id, query.SearchTerm));
        Assert.False(query.IsEmpty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("!?")]
    [InlineData("é.")]
    public void TooShortIsEmpty(string term) => Assert.True(LookupQuery.Parse(term).IsEmpty);

    [Fact]
    public void RankMergesSourcesByTitleMatchThenYear()
    {
        DiscoveryItem[] movies = [Movie("Dune: Part Two", 2024), Movie("Dune", 1984), Movie("Dune", 2021)];
        DiscoveryItem[] series = [Series("Dune: Prophecy", 2024), Series("Frank Herbert's Dune", 2000)];

        IReadOnlyList<DiscoveryItem> ranked = LookupQuery.Parse("dune 2021").Rank([movies, series]);

        Assert.Equal(
            ["Dune 2021", "Dune 1984", "Dune: Part Two 2024", "Dune: Prophecy 2024", "Frank Herbert's Dune 2000"],
            ranked.Select(i => $"{i.Title} {i.Year}"));
    }

    [Fact]
    public void RankKeepsSourceOrderBetweenEqualMatches()
    {
        DiscoveryItem[] movies = [Movie("The Office Party", 2010), Movie("The Office Space", 2012)];
        DiscoveryItem[] series = [Series("The Office", 2005), Series("The Office", 2001)];

        IReadOnlyList<DiscoveryItem> ranked = LookupQuery.Parse("the office").Rank([movies, series]);

        Assert.Equal([2005, 2001, 2010, 2012], ranked.Select(i => i.Year));
    }

    [Fact]
    public void TitleEndingInAYearBeatsTheYearFilter()
    {
        DiscoveryItem[] movies = [Movie("Blade Runner", 1982), Movie("Blade Runner 2049", 2017)];

        IReadOnlyList<DiscoveryItem> ranked = LookupQuery.Parse("Blade Runner 2049").Rank([movies]);

        Assert.Equal("Blade Runner 2049", ranked[0].Title);
    }

    private static DiscoveryItem Movie(string title, int year) => new() { Kind = MediaType.Movie, Title = title, Year = year };

    private static DiscoveryItem Series(string title, int year) => new() { Kind = MediaType.Series, Title = title, Year = year };
}
