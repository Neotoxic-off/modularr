using Modularr.Core.Configuration;
using Modularr.Core.Downloads;
using Modularr.Core.Downloads.Torrents;
using Modularr.Core.Library;
using Modularr.Core.Persistence;

namespace Modularr.Core.Tests.Library;

public sealed class ArrCompatTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    [Fact]
    public void MapsALibrarySeriesToTheSonarrShape()
    {
        string path = Path.Combine(Path.GetTempPath(), "anime", "Frieren (2023)");
        Series series = new()
        {
            Id = 7, TvdbId = 10, Title = "Frieren", SortTitle = "frieren", Year = 2023, Path = path, QualityProfileId = 2, Monitored = true,
            SeriesType = SeriesType.Anime, Poster = "https://img/p.jpg", Added = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        series.Seasons.Add(new Season { Number = 0, Monitored = false });
        series.Seasons.Add(new Season { Number = 1, Monitored = true });
        series.Episodes.Add(new Episode { SeasonNumber = 1, EpisodeNumber = 1, AirDate = new DateOnly(2026, 9, 30), Monitored = true, MediaFileId = 3 });
        series.Episodes.Add(new Episode { SeasonNumber = 1, EpisodeNumber = 2, AirDate = new DateOnly(2026, 10, 12), Monitored = true });
        series.Episodes.Add(new Episode { SeasonNumber = 1, EpisodeNumber = 3, AirDate = new DateOnly(2026, 11, 1), Monitored = true });

        ArrSeries sonarr = ArrCompat.ToSonarr(series, Today);

        Assert.Equal((7, 10, "Frieren", 2023, true, 2), (sonarr.Id, sonarr.TvdbId, sonarr.Title, sonarr.Year, sonarr.Monitored, sonarr.QualityProfileId));
        Assert.Equal("anime", sonarr.SeriesType);
        Assert.Equal(Path.GetDirectoryName(path), sonarr.RootFolderPath);
        Assert.Equal([(0, false), (1, true)], sonarr.Seasons.Select(s => (s.SeasonNumber, s.Monitored)));
        // Aired and monitored, or with a file: 2 of the 3 episodes; one file.
        Assert.Equal(new ArrStatistics(1, 2, 3, 50m, SeasonCount: 1), sonarr.Statistics);
        Assert.Equal(new ArrStatistics(1, 2, 3, 50m), sonarr.Seasons[1].Statistics);
        Assert.Equal("poster", Assert.Single(sonarr.Images).CoverType);
    }

    [Fact]
    public void MapsALibraryMovieToTheRadarrShape()
    {
        Movie movie = new() { Id = 4, TmdbId = 603, ImdbId = "tt0133093", Title = "The Matrix", Year = 1999, Path = Path.Combine("movies", "The Matrix (1999)"), QualityProfileId = 1, Monitored = true };

        ArrMovie radarr = ArrCompat.ToRadarr(movie, hasFile: true);

        Assert.Equal((4, 603, "tt0133093", 1999, true, true, 1), (radarr.Id, radarr.TmdbId, radarr.ImdbId, radarr.Year, radarr.Monitored, radarr.HasFile, radarr.QualityProfileId));
        Assert.Equal("movies", radarr.RootFolderPath);
        Assert.Empty(radarr.Images);
    }

    [Fact]
    public void ApiKeyMatchesOnlyTheConfiguredKey()
    {
        string key = AuthOptions.GenerateApiKey();

        Assert.Matches("^[0-9a-f]{64}$", key);
        Assert.NotEqual(key, AuthOptions.GenerateApiKey());
        Assert.True(AuthOptions.ApiKeyMatches(key, key));
        Assert.False(AuthOptions.ApiKeyMatches(key, key.ToUpperInvariant()));
        Assert.False(AuthOptions.ApiKeyMatches(key, key[..^1]));
        Assert.False(AuthOptions.ApiKeyMatches(key, ""));
        Assert.False(AuthOptions.ApiKeyMatches(key, null));
        // No key configured: key sign-in is off, whatever is presented.
        Assert.False(AuthOptions.ApiKeyMatches(null, ""));
        Assert.False(AuthOptions.ApiKeyMatches("", ""));
        Assert.False(AuthOptions.ApiKeyMatches(null, key));
    }

    [Fact]
    public void RedactedOptionsHideTheApiKey()
    {
        ModularrOptions options = new() { Auth = new AuthOptions { ApiKey = "secret" } };

        Assert.Null(options.Redacted().Auth.ApiKey);
        Assert.Equal("secret", options.Auth.ApiKey);
    }

    [Fact]
    public void MapsActiveDownloadsToTheQueueShapeAPageAtATime()
    {
        DateTime now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        TorrentStatus torrent = new()
        {
            Id = "abc", Name = "Movie.2024.1080p", Category = "movies", Status = DownloadItemStatus.Downloading, Progress = 0.25, TotalSize = 4000, RemainingSize = 3000,
            RemainingTime = new TimeSpan(1, 2, 3, 4), DownloadRate = 1, UploadRate = 0, SeedRatio = 0, SavePath = "/d", OutputPath = "/d/m", Files = [], CanBeRemoved = false,
        };
        DownloadItem movie = new(new TrackedDownload { Id = 3, InfoHash = "abc", MovieId = 9, ReleaseTitle = "Movie.2024.1080p" }, torrent);
        DownloadItem episode = new(new TrackedDownload { Id = 2, InfoHash = "def", SeriesId = 4, EpisodeIds = [40], ReleaseTitle = "Show.S01E01", State = TrackedDownloadState.ImportBlocked }, null);
        DownloadItem season = new(new TrackedDownload { Id = 1, InfoHash = "fed", SeriesId = 4, EpisodeIds = [40, 41], ReleaseTitle = "Show.S01" }, null);

        ArrQueuePage first = ArrCompat.Queue([movie, episode, season], page: 1, pageSize: 2, now, new Dictionary<int, Movie> { [9] = new() { Id = 9, TmdbId = 99, Title = "Movie", Path = "/m" } });

        Assert.Equal((1, 2, 3), (first.Page, first.PageSize, first.TotalRecords));
        ArrQueueItem downloading = first.Records[0];
        Assert.Equal((3, 4000L, 3000L, "downloading", "downloading", "26:03:04", "ABC"), (downloading.Id, downloading.Size, downloading.Sizeleft, downloading.Status, downloading.TrackedDownloadState, downloading.Timeleft, downloading.DownloadId));
        Assert.Equal(now + new TimeSpan(1, 2, 3, 4), downloading.EstimatedCompletionTime);
        Assert.Equal(new ArrQueueMovie(99, "Movie"), downloading.Movie);
        ArrQueueItem blocked = first.Records[1];
        Assert.Equal(("queued", "importBlocked", 40, (string?)null, (ArrQueueSeries?)null), (blocked.Status, blocked.TrackedDownloadState, blocked.EpisodeId, blocked.Timeleft, blocked.Series));

        ArrQueueItem last = Assert.Single(ArrCompat.Queue([movie, episode, season], page: 2, pageSize: 2, now).Records);
        Assert.Equal((1, (int?)null, (ArrQueueMovie?)null), (last.Id, last.EpisodeId, last.Movie));
    }
}
