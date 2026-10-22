using Modularr.Core.Decisions;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;
using Modularr.Core.Search;
using Modularr.Core.Tests.Persistence;

namespace Modularr.Core.Tests.Search;

public sealed class PresentFilesTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public void AFileIsReplacedOnlyByAnUpgradeBelowTheCutoff()
    {
        (QualityProfile profile, Quality below, Quality cutoff) = Qualities();

        Assert.True(PresentFiles.IsWanted(null, profile, upgrade: false));
        Assert.False(PresentFiles.IsWanted(File(below), profile, upgrade: false));
        Assert.True(PresentFiles.IsWanted(File(below), profile, upgrade: true));
        Assert.False(PresentFiles.IsWanted(File(cutoff), profile, upgrade: true));
    }

    [Fact]
    public async Task AMovieWithAFileIsRefusedUnlessUpgradingBelowTheCutoff()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (_, Quality below, _) = Qualities();
        Movie movie = _database.AddMovie(603);
        using ModularrDbContext db = _database.NewContext();
        await PresentFiles.EnsureMovieWantedAsync(db, movie.Id, upgrade: false, ct);

        _database.Insert(File(below, movieId: movie.Id));

        AlreadyPresentException refused = await Assert.ThrowsAsync<AlreadyPresentException>(() => PresentFiles.EnsureMovieWantedAsync(db, movie.Id, upgrade: false, ct));
        Assert.Contains("Movie 603 already has a file", refused.Message, StringComparison.Ordinal);
        await PresentFiles.EnsureMovieWantedAsync(db, movie.Id, upgrade: true, ct);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => PresentFiles.EnsureMovieWantedAsync(db, 999, upgrade: false, ct));
    }

    [Fact]
    public async Task ASeasonIsRefusedOnceEveryMonitoredEpisodeHasAFile()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (_, _, Quality cutoff) = Qualities();
        Series series = _database.AddSeries(42);
        MediaFile file = _database.Insert(File(cutoff, seriesId: series.Id))[0];
        _database.Insert(
            new Episode { SeriesId = series.Id, SeasonNumber = 1, EpisodeNumber = 1, Monitored = true, MediaFileId = file.Id },
            new Episode { SeriesId = series.Id, SeasonNumber = 1, EpisodeNumber = 2, Monitored = false },
            new Episode { SeriesId = series.Id, SeasonNumber = 2, EpisodeNumber = 1, Monitored = true });
        using ModularrDbContext db = _database.NewContext();

        await Assert.ThrowsAsync<AlreadyPresentException>(() =>
            PresentFiles.EnsureEpisodesWantedAsync(db, series.Id, e => e.Monitored && e.SeasonNumber == 1, "Season 1", upgrade: true, ct));
        await PresentFiles.EnsureEpisodesWantedAsync(db, series.Id, e => e.Monitored && e.SeasonNumber == 2, "Season 2", upgrade: false, ct);
        await PresentFiles.EnsureEpisodesWantedAsync(db, series.Id, e => e.Monitored, "Series", upgrade: false, ct);

        // Nothing in scope (no monitored episode): nothing to refuse.
        await PresentFiles.EnsureEpisodesWantedAsync(db, series.Id, e => e.Monitored && e.SeasonNumber == 3, "Season 3", upgrade: false, ct);
    }

    private (QualityProfile Profile, Quality Below, Quality Cutoff) Qualities()
    {
        using ModularrDbContext db = _database.NewContext();
        QualityProfile profile = db.QualityProfiles.Single(p => p.Id == 1);

        // Upgrades are off in the default profiles: the cutoff is the first allowed quality.
        return (profile, Quality.Unknown, profile.FirstAllowedQuality());
    }

    private static MediaFile File(Quality quality, int? movieId = null, int? seriesId = null) =>
        new() { MovieId = movieId, SeriesId = seriesId, Path = $"/media/{Guid.NewGuid():N}.mkv", Quality = new QualityModel(quality, new Revision()) };
}
