using Microsoft.EntityFrameworkCore;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;

namespace Modularr.Core.Tests.Persistence;

// Delete rules are enforced by SQLite itself (ExecuteDelete bypasses the change tracker).
public sealed class CascadeTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static readonly QualityModel Hdtv = new(Quality.Hdtv720p, new Revision());

    [Fact]
    public void DeletingASeriesDeletesEverythingThatBelongsToIt()
    {
        Series series = _database.AddSeries(1);
        Series kept = _database.AddSeries(2);
        MediaFile file = _database.Insert(new MediaFile { SeriesId = series.Id, Path = "/tv/1/e1.mkv", Quality = Hdtv })[0];
        Episode episode = _database.Insert(new Episode { SeriesId = series.Id, SeasonNumber = 1, EpisodeNumber = 1, MediaFileId = file.Id })[0];
        _database.Insert(new Episode { SeriesId = kept.Id, SeasonNumber = 1, EpisodeNumber = 1 });
        _database.Insert(new HistoryItem { SeriesId = series.Id, EpisodeId = episode.Id, SourceTitle = "t", Quality = Hdtv });
        _database.Insert(new BlocklistItem { SeriesId = series.Id, SourceTitle = "t", Quality = Hdtv });
        _database.Insert(new TrackedDownload { SeriesId = series.Id, InfoHash = "aa", ReleaseTitle = "t" });

        using ModularrDbContext db = _database.NewContext();
        db.Series.Where(s => s.Id == series.Id).ExecuteDelete();

        Assert.Equal(kept.Id, db.Episodes.Single().SeriesId);
        Assert.Empty(db.MediaFiles);
        Assert.Empty(db.History);
        Assert.Empty(db.Blocklist);
        Assert.Empty(db.TrackedDownloads);
    }

    [Fact]
    public void DeletingAMovieDeletesEverythingThatBelongsToIt()
    {
        Movie movie = _database.AddMovie(1);
        _database.Insert(new MediaFile { MovieId = movie.Id, Path = "/movies/1.mkv", Quality = Hdtv });
        _database.Insert(new HistoryItem { MovieId = movie.Id, SourceTitle = "t", Quality = Hdtv });
        _database.Insert(new BlocklistItem { MovieId = movie.Id, SourceTitle = "t", Quality = Hdtv });
        _database.Insert(new TrackedDownload { MovieId = movie.Id, InfoHash = "aa", ReleaseTitle = "t" });

        using ModularrDbContext db = _database.NewContext();
        db.Movies.Where(m => m.Id == movie.Id).ExecuteDelete();

        Assert.Empty(db.MediaFiles);
        Assert.Empty(db.History);
        Assert.Empty(db.Blocklist);
        Assert.Empty(db.TrackedDownloads);
    }

    [Fact]
    public void DeletingAMediaFileUnlinksItsEpisodes()
    {
        Series series = _database.AddSeries(1);
        MediaFile file = _database.Insert(new MediaFile { SeriesId = series.Id, Path = "/tv/1/e1e2.mkv", Quality = Hdtv })[0];
        _database.Insert(
            new Episode { SeriesId = series.Id, SeasonNumber = 1, EpisodeNumber = 1, MediaFileId = file.Id },
            new Episode { SeriesId = series.Id, SeasonNumber = 1, EpisodeNumber = 2, MediaFileId = file.Id });

        using ModularrDbContext db = _database.NewContext();
        db.MediaFiles.Where(f => f.Id == file.Id).ExecuteDelete();

        Assert.Equal(2, db.Episodes.Count(e => e.MediaFileId == null));
    }

    [Fact]
    public void DeletingAnEpisodeDeletesItsHistory()
    {
        Series series = _database.AddSeries(1);
        Episode episode = _database.Insert(new Episode { SeriesId = series.Id, SeasonNumber = 1, EpisodeNumber = 1 })[0];
        _database.Insert(new HistoryItem { SeriesId = series.Id, EpisodeId = episode.Id, SourceTitle = "t", Quality = Hdtv });

        using ModularrDbContext db = _database.NewContext();
        db.Episodes.Where(e => e.Id == episode.Id).ExecuteDelete();

        Assert.Empty(db.History);
    }

    [Fact]
    public void AQualityProfileInUseCannotBeDeleted()
    {
        _database.AddSeries(1);

        using ModularrDbContext db = _database.NewContext();
        db.QualityProfiles.Remove(db.QualityProfiles.Single(p => p.Id == 1));

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        Assert.Equal(1, db.QualityProfiles.Where(p => p.Id == 2).ExecuteDelete());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ARowBelongsToExactlyOneOfSeriesOrMovie(bool series, bool movie)
    {
        int? seriesId = series ? _database.AddSeries(1).Id : null;
        int? movieId = movie ? _database.AddMovie(1).Id : null;

        Assert.Throws<DbUpdateException>(() => _database.Insert(new MediaFile { SeriesId = seriesId, MovieId = movieId, Path = "/x.mkv", Quality = Hdtv }));
        Assert.Throws<DbUpdateException>(() => _database.Insert(new BlocklistItem { SeriesId = seriesId, MovieId = movieId, SourceTitle = "t", Quality = Hdtv }));
        Assert.Throws<DbUpdateException>(() => _database.Insert(new HistoryItem { SeriesId = seriesId, MovieId = movieId, SourceTitle = "t", Quality = Hdtv }));
        Assert.Throws<DbUpdateException>(() => _database.Insert(new TrackedDownload { SeriesId = seriesId, MovieId = movieId, InfoHash = "aa", ReleaseTitle = "t" }));
    }

    [Fact]
    public void AnEpisodeIsUniquePerSeriesSeasonAndNumber()
    {
        Series series = _database.AddSeries(1);
        _database.Insert(new Episode { SeriesId = series.Id, SeasonNumber = 1, EpisodeNumber = 1 });

        Assert.Throws<DbUpdateException>(() => _database.Insert(new Episode { SeriesId = series.Id, SeasonNumber = 1, EpisodeNumber = 1 }));
    }
}
