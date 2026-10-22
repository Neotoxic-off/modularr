using Microsoft.EntityFrameworkCore;
using Modularr.Core.Languages;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;

namespace Modularr.Core.Tests.Persistence;

// Every entity written in one context and read back in a fresh one.
public sealed class MappingTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public void SeriesRoundTripsWithSeasonsAndEpisodes()
    {
        DateTime added = new(2024, 5, 1, 12, 30, 0, DateTimeKind.Utc);
        Series series = new()
        {
            TvdbId = 81189,
            Title = "Breaking Bad",
            SortTitle = "breaking bad",
            Year = 2008,
            SeriesType = SeriesType.Daily,
            Monitored = true,
            Path = "/tv/Breaking Bad",
            QualityProfileId = 4,
            Added = added,
        };
        series.Seasons.Add(new Season { Number = 0, Monitored = false });
        series.Seasons.Add(new Season { Number = 1, Monitored = true });
        series.Episodes.Add(new Episode
        {
            SeasonNumber = 1,
            EpisodeNumber = 2,
            AbsoluteEpisodeNumber = 2,
            AirDate = new DateOnly(2008, 1, 27),
            Title = "Cat's in the Bag...",
            Monitored = true,
        });
        _database.Insert(series);

        using ModularrDbContext db = _database.NewContext();
        Series stored = db.Series.Include(s => s.Episodes).Include(s => s.QualityProfile).Single();

        Assert.Equal((81189, "Breaking Bad", "breaking bad", 2008, SeriesType.Daily, true, "/tv/Breaking Bad"),
            (stored.TvdbId, stored.Title, stored.SortTitle, stored.Year, stored.SeriesType, stored.Monitored, stored.Path));
        Assert.Equal(added, stored.Added);
        Assert.Equal(DateTimeKind.Utc, stored.Added.Kind);
        Assert.Equal("HD-1080p", stored.QualityProfile.Name);
        Assert.Equal([(0, false), (1, true)], stored.Seasons.Select(s => (s.Number, s.Monitored)));
        Episode episode = Assert.Single(stored.Episodes);
        Assert.Equal((1, 2, 2, new DateOnly(2008, 1, 27), "Cat's in the Bag...", true, (int?)null),
            (episode.SeasonNumber, episode.EpisodeNumber, episode.AbsoluteEpisodeNumber, episode.AirDate, episode.Title, episode.Monitored, episode.MediaFileId));
    }

    // Sonarr SeriesRepositoryFixture.should_lazyload_quality_profile, Radarr MovieRepositoryFixture.should_load_quality_profile.
    [Fact]
    public void MovieRoundTripsAndLoadsItsQualityProfile()
    {
        _database.Insert(new Movie
        {
            TmdbId = 603,
            ImdbId = "tt0133093",
            Title = "The Matrix",
            Year = 1999,
            Monitored = true,
            Path = "/movies/The Matrix (1999)",
            QualityProfileId = 5,
        });

        using ModularrDbContext db = _database.NewContext();
        Movie stored = db.Movies.Include(m => m.QualityProfile).Single();

        Assert.Equal((603, "tt0133093", "The Matrix", 1999, true, "/movies/The Matrix (1999)"),
            (stored.TmdbId, stored.ImdbId, stored.Title, stored.Year, stored.Monitored, stored.Path));
        Assert.Equal("Ultra-HD", stored.QualityProfile.Name);
    }

    [Fact]
    public void MediaFileRoundTripsQualityAndLanguages()
    {
        int seriesId = _database.AddSeries(1).Id;
        QualityModel quality = new(Quality.WebDl1080p, new Revision(2, 1, IsRepack: true)) { SourceDetectionSource = QualityDetectionSource.Name };
        _database.Insert(new MediaFile
        {
            SeriesId = seriesId,
            Path = "/tv/Series 1/S01E01.mkv",
            Size = 3_000_000_000,
            Quality = quality,
            Languages = [Language.French, Language.English],
            ReleaseGroup = "NTb",
            DateAdded = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        });

        using ModularrDbContext db = _database.NewContext();
        MediaFile stored = db.MediaFiles.Single();

        Assert.Equal(quality, stored.Quality);
        Assert.Same(Quality.WebDl1080p, stored.Quality.Quality);
        Assert.Equal((2, 1, true), (stored.Quality.Revision.Version, stored.Quality.Revision.Real, stored.Quality.Revision.IsRepack));
        Assert.Equal([Language.French, Language.English], stored.Languages);
        Assert.Equal((3_000_000_000L, "NTb", "/tv/Series 1/S01E01.mkv"), (stored.Size, stored.ReleaseGroup, stored.Path));
    }

    [Fact]
    public void QualityAndLanguageAreStoredById()
    {
        int movieId = _database.AddMovie(1).Id;
        _database.Insert(new MediaFile
        {
            MovieId = movieId,
            Path = "/movies/1.mkv",
            Quality = new QualityModel(Quality.Remux2160p, new Revision()),
            Languages = [Language.English, Language.PortugueseBrazil],
        });

        using ModularrDbContext db = _database.NewContext();

        Assert.Equal(Quality.Bluray1080p.Id, db.Database.SqlQueryRaw<int>("SELECT Cutoff AS Value FROM QualityProfiles WHERE Name = 'HD-1080p'").Single());
        Assert.Equal("""{"Quality":31,"Revision":{"Version":1,"Real":0,"IsRepack":false}}""", db.Database.SqlQueryRaw<string>("SELECT Quality AS Value FROM MediaFiles").Single());
        Assert.Equal("[1,30]", db.Database.SqlQueryRaw<string>("SELECT Languages AS Value FROM MediaFiles").Single());
    }

    [Fact]
    public void UnknownStoredQualityIdFailsInsteadOfFallingBack()
    {
        _database.AddMovie(1);
        using ModularrDbContext db = _database.NewContext();
        db.Database.ExecuteSqlRaw("UPDATE QualityProfiles SET Cutoff = 999 WHERE Id = 1");

        Assert.Throws<ArgumentOutOfRangeException>(() => db.QualityProfiles.Single(p => p.Id == 1));
    }

    [Fact]
    public void InPlaceChangesToJsonColumnsAreSaved()
    {
        HistoryItem history = new()
        {
            EventType = HistoryEventType.Imported,
            SeriesId = _database.AddSeries(1).Id,
            SourceTitle = "title",
            Quality = new QualityModel(Quality.Hdtv720p, new Revision()),
        };
        _database.Insert(history);

        using (ModularrDbContext db = _database.NewContext())
        {
            HistoryItem tracked = db.History.Single();
            tracked.Data["downloadClient"] = "modularr";
            tracked.Quality = tracked.Quality with { Revision = new Revision(1, 0, IsRepack: true) };
            db.SaveChanges();
        }

        using ModularrDbContext reread = _database.NewContext();
        HistoryItem stored = reread.History.Single();
        Assert.Equal("modularr", stored.Data["downloadClient"]);
        Assert.True(stored.Quality.Revision.IsRepack);
    }

    [Fact]
    public void TrackedDownloadRoundTrips()
    {
        int seriesId = _database.AddSeries(1).Id;
        _database.Insert(new TrackedDownload
        {
            InfoHash = "8a19577fb5f690970ca43a57ff1011ae202244b8",
            SeriesId = seriesId,
            EpisodeIds = [3, 4],
            ReleaseTitle = "Series.S01E03E04.1080p.WEB-DL",
            Indexer = "Nyaa",
            State = TrackedDownloadState.ImportPending,
        });

        using ModularrDbContext db = _database.NewContext();
        TrackedDownload stored = db.TrackedDownloads.Single(d => d.InfoHash == "8A19577FB5F690970CA43A57FF1011AE202244B8");

        Assert.Equal([3, 4], stored.EpisodeIds);
        Assert.Equal((seriesId, "Series.S01E03E04.1080p.WEB-DL", "Nyaa", TrackedDownloadState.ImportPending),
            (stored.SeriesId, stored.ReleaseTitle, stored.Indexer, stored.State));
    }

    [Fact]
    public void TrackedDownloadInfoHashIsUniqueIgnoringCase()
    {
        int movieId = _database.AddMovie(1).Id;
        _database.Insert(new TrackedDownload { InfoHash = "abcdef", MovieId = movieId, ReleaseTitle = "a" });

        Assert.Throws<DbUpdateException>(() => _database.Insert(new TrackedDownload { InfoHash = "ABCDEF", MovieId = movieId, ReleaseTitle = "b" }));
    }
}
