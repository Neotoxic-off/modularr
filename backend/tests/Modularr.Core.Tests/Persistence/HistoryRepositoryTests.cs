using Microsoft.EntityFrameworkCore;
using Modularr.Core.Languages;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;

namespace Modularr.Core.Tests.Persistence;

// Sonarr + Radarr HistoryRepositoryFixture; FindDownloadHistory is the upstream query written against the DbSet.
public sealed class HistoryRepositoryTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static HistoryItem Grabbed(int? seriesId, int? movieId, Quality quality) => new()
    {
        EventType = HistoryEventType.Grabbed,
        SeriesId = seriesId,
        MovieId = movieId,
        SourceTitle = "title",
        Quality = new QualityModel(quality, new Revision()),
        Languages = [Language.English],
        Date = DateTime.UtcNow,
    };

    private static List<HistoryItem> FindDownloadHistory(ModularrDbContext db, int? seriesId, int? movieId, QualityModel quality) =>
        [.. db.History.Where(h => h.SeriesId == seriesId && h.MovieId == movieId && h.Quality == quality &&
            (h.EventType == HistoryEventType.Grabbed || h.EventType == HistoryEventType.Failed || h.EventType == HistoryEventType.Imported))];

    [Fact]
    public void ShouldReadWriteDictionary()
    {
        HistoryItem history = Grabbed(_database.AddSeries(1).Id, null, Quality.Unknown);
        history.Data.Add("key1", "value1");
        history.Data.Add("key2", "value2");
        _database.Insert(history);

        using ModularrDbContext db = _database.NewContext();
        Assert.Equal(2, db.History.Single().Data.Count);
    }

    [Fact]
    public void ShouldGetDownloadHistoryForSeries()
    {
        int seriesId = _database.AddSeries(12).Id;
        _database.Insert(Grabbed(seriesId, null, Quality.Bluray1080p), Grabbed(seriesId, null, Quality.Dvd));

        using ModularrDbContext db = _database.NewContext();
        Assert.Single(FindDownloadHistory(db, seriesId, null, new QualityModel(Quality.Bluray1080p, new Revision())));
    }

    [Fact]
    public void ShouldGetDownloadHistoryForMovie()
    {
        int movieId = _database.AddMovie(12).Id;
        _database.Insert(Grabbed(null, movieId, Quality.Bluray1080p), Grabbed(null, movieId, Quality.Dvd));

        using ModularrDbContext db = _database.NewContext();
        Assert.Single(FindDownloadHistory(db, null, movieId, new QualityModel(Quality.Bluray1080p, new Revision())));
    }

    [Fact]
    public void ShouldDeleteHistoryItemsBySeriesId()
    {
        Series series1 = _database.AddSeries(7);
        Series series2 = _database.AddSeries(8);
        _database.Insert(
            Grabbed(series2.Id, null, Quality.Bluray1080p),
            Grabbed(series1.Id, null, Quality.Bluray1080p),
            Grabbed(series1.Id, null, Quality.Bluray1080p),
            Grabbed(series1.Id, null, Quality.Bluray1080p),
            Grabbed(series1.Id, null, Quality.Bluray1080p));

        using ModularrDbContext db = _database.NewContext();
        db.Series.Where(s => s.Id == series1.Id).ExecuteDelete();

        Assert.Equal(0, db.History.Count(h => h.SeriesId == series1.Id));
        Assert.Equal(1, db.History.Count(h => h.SeriesId == series2.Id));
    }
}
