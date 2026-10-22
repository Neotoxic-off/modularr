using Microsoft.EntityFrameworkCore;
using Modularr.Core.Languages;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;

namespace Modularr.Core.Tests.Persistence;

// Sonarr + Radarr BlocklistRepositoryFixture; DeleteForSeriesIds/DeleteForMovies are the FK cascade here.
public sealed class BlocklistRepositoryTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static BlocklistItem ForSeries(int seriesId, string sourceTitle = "series.title.s01e01", string? infoHash = null) => new()
    {
        SeriesId = seriesId,
        EpisodeIds = [1],
        Quality = new QualityModel(Quality.Bluray720p, new Revision()),
        Languages = [Language.English],
        SourceTitle = sourceTitle,
        InfoHash = infoHash,
        Date = DateTime.UtcNow,
    };

    private static BlocklistItem ForMovie(int movieId) => new()
    {
        MovieId = movieId,
        Quality = new QualityModel(Quality.Unknown, new Revision()),
        SourceTitle = "movie.title.1998",
        Date = DateTime.UtcNow,
    };

    [Fact]
    public void ShouldBeAbleToWriteToDatabase()
    {
        _database.Insert(ForSeries(_database.AddSeries(12345).Id), ForMovie(_database.AddMovie(1234).Id));

        using ModularrDbContext db = _database.NewContext();
        Assert.Equal(2, db.Blocklist.Count());
    }

    [Fact]
    public void ShouldHaveEpisodeIds()
    {
        _database.Insert(ForSeries(_database.AddSeries(12345).Id));

        using ModularrDbContext db = _database.NewContext();
        Assert.Equal([1], db.Blocklist.Single().EpisodeIds);
    }

    [Fact]
    public void ShouldHaveMovieId()
    {
        int movieId = _database.AddMovie(1234).Id;
        _database.Insert(ForMovie(movieId));

        using ModularrDbContext db = _database.NewContext();
        Assert.Equal(movieId, db.Blocklist.Single().MovieId);
    }

    [Theory]
    [InlineData("series.title.s01e01")]
    [InlineData("movie.title.1998")]
    public void ShouldCheckForBlocklistedTitleCaseInsensitive(string sourceTitle)
    {
        int seriesId = _database.AddSeries(12345).Id;
        _database.Insert(ForSeries(seriesId, sourceTitle));
        string upper = sourceTitle.ToUpperInvariant();

        using ModularrDbContext db = _database.NewContext();
        Assert.Single(db.Blocklist.Where(b => b.SeriesId == seriesId && b.SourceTitle == upper));
    }

    [Fact]
    public void ShouldDeleteBlocklistsBySeriesId()
    {
        Series series1 = _database.AddSeries(7);
        Series series2 = _database.AddSeries(8);
        _database.Insert(ForSeries(series2.Id), ForSeries(series1.Id), ForSeries(series1.Id), ForSeries(series1.Id), ForSeries(series1.Id));

        using ModularrDbContext db = _database.NewContext();
        db.Series.Where(s => s.Id == series1.Id).ExecuteDelete();

        Assert.Equal(0, db.Blocklist.Count(b => b.SeriesId == series1.Id));
        Assert.Equal(1, db.Blocklist.Count(b => b.SeriesId == series2.Id));
    }

    [Fact]
    public void ShouldDeleteBlocklistsByMovieId()
    {
        Movie movie1 = _database.AddMovie(7);
        Movie movie2 = _database.AddMovie(8);
        _database.Insert(ForMovie(movie2.Id), ForMovie(movie1.Id), ForMovie(movie1.Id), ForMovie(movie1.Id), ForMovie(movie1.Id));

        using ModularrDbContext db = _database.NewContext();
        db.Movies.Where(m => m.Id == movie1.Id).ExecuteDelete();

        Assert.Equal(0, db.Blocklist.Count(b => b.MovieId == movie1.Id));
        Assert.Equal(1, db.Blocklist.Count(b => b.MovieId == movie2.Id));
    }

    [Fact]
    public void InfoHashIsUniqueIgnoringCase()
    {
        int seriesId = _database.AddSeries(1).Id;
        _database.Insert(ForSeries(seriesId, infoHash: "8a19577fb5f690970ca43a57ff1011ae202244b8"));

        Assert.Throws<DbUpdateException>(() => _database.Insert(ForSeries(seriesId, "other.title", "8A19577FB5F690970CA43A57FF1011AE202244B8")));
    }

    [Fact]
    public void ManyItemsMayHaveNoInfoHash()
    {
        int seriesId = _database.AddSeries(1).Id;
        _database.Insert(ForSeries(seriesId), ForSeries(seriesId));

        using ModularrDbContext db = _database.NewContext();
        Assert.Equal(2, db.Blocklist.Count());
    }
}
