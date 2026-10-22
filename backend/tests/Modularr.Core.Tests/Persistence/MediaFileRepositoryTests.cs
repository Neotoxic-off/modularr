using Microsoft.EntityFrameworkCore;
using Modularr.Core.Languages;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;

namespace Modularr.Core.Tests.Persistence;

// Sonarr + Radarr MediaFileRepositoryFixture; GetFilesBySeries/Movie are plain queries, DeleteForSeries is the FK cascade.
public sealed class MediaFileRepositoryTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static MediaFile File(int? seriesId, int? movieId) => new()
    {
        SeriesId = seriesId,
        MovieId = movieId,
        Path = $"/media/{Guid.NewGuid():N}.mkv",
        Quality = new QualityModel(Quality.Bluray720p, new Revision()),
        Languages = [Language.English],
    };

    [Fact]
    public void GetFilesBySeries()
    {
        int seriesId = _database.AddSeries(12).Id;
        int otherId = _database.AddSeries(13).Id;
        _database.Insert([.. Enumerable.Range(0, 10).Select(i => File(i % 3 == 0 ? seriesId : otherId, null))]);

        using ModularrDbContext db = _database.NewContext();
        List<MediaFile> seriesFiles = [.. db.MediaFiles.Where(f => f.SeriesId == seriesId)];

        Assert.Equal(4, seriesFiles.Count);
        Assert.All(seriesFiles, f => Assert.Equal(seriesId, f.SeriesId));
    }

    [Fact]
    public void GetFilesByMovie()
    {
        int movieId = _database.AddMovie(12).Id;
        int otherId = _database.AddMovie(13).Id;
        _database.Insert([.. Enumerable.Range(0, 10).Select(i => File(null, i % 3 == 0 ? movieId : otherId))]);

        using ModularrDbContext db = _database.NewContext();
        List<MediaFile> movieFiles = [.. db.MediaFiles.Where(f => f.MovieId == movieId)];

        Assert.Equal(4, movieFiles.Count);
        Assert.All(movieFiles, f => Assert.Equal(movieId, f.MovieId));
    }

    [Fact]
    public void ShouldDeleteFilesBySeriesId()
    {
        Series series1 = _database.AddSeries(7);
        Series series2 = _database.AddSeries(8);
        _database.Insert(File(series2.Id, null), File(series1.Id, null), File(series1.Id, null), File(series1.Id, null), File(series1.Id, null));

        using ModularrDbContext db = _database.NewContext();
        db.Series.Where(s => s.Id == series1.Id).ExecuteDelete();

        Assert.Equal(0, db.MediaFiles.Count(f => f.SeriesId == series1.Id));
        Assert.Equal(1, db.MediaFiles.Count(f => f.SeriesId == series2.Id));
    }

    [Fact]
    public void ShouldDeleteFilesByMovieId()
    {
        Movie movie1 = _database.AddMovie(7);
        Movie movie2 = _database.AddMovie(8);
        _database.Insert(File(null, movie2.Id), File(null, movie1.Id), File(null, movie1.Id), File(null, movie1.Id), File(null, movie1.Id));

        using ModularrDbContext db = _database.NewContext();
        db.Movies.Where(m => m.Id == movie1.Id).ExecuteDelete();

        Assert.Equal(0, db.MediaFiles.Count(f => f.MovieId == movie1.Id));
        Assert.Equal(1, db.MediaFiles.Count(f => f.MovieId == movie2.Id));
    }
}
