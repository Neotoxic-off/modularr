using Modularr.Core.Decisions;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;
using Modularr.Core.Search;
using Modularr.Core.Tests.Persistence;

namespace Modularr.Core.Tests.Search;

public sealed class ScheduledSearchTests
{
    private static readonly DateOnly Today = new(2026, 10, 23);

    [Fact]
    public async Task OnlyMonitoredTitlesWithAnAiredOrReleasedItemMissingOrBelowTheCutoffAreWanted()
    {
        using TestDatabase database = new();
        Series missing = database.Insert(Show(1, monitored: true, Today.AddDays(-1)))[0];
        Series complete = database.Insert(Show(2, monitored: true, Today.AddDays(-1)))[0];
        database.Insert(Show(3, monitored: false, Today.AddDays(-1)));
        database.Insert(Show(4, monitored: true, Today.AddDays(1)));
        Movie released = database.Insert(Film(10, Today.AddDays(-30)))[0];
        database.Insert(Film(11, Today.AddDays(30)));
        Movie meetsCutoff = database.Insert(Film(12, Today.AddDays(-30)))[0];
        Movie belowCutoff = database.Insert(Film(13, Today.AddDays(-30)))[0];
        Movie downloading = database.Insert(Film(14, Today.AddDays(-30)))[0];
        (Quality cutoff, Quality lower) = Qualities(database);
        MediaFile episodeFile = database.Insert(new MediaFile { SeriesId = complete.Id, Path = "/tv/2/e.mkv", Quality = new QualityModel(cutoff, new Revision()) })[0];
        database.Insert(
            new MediaFile { MovieId = meetsCutoff.Id, Path = "/movies/12/m.mkv", Quality = new QualityModel(cutoff, new Revision()) },
            new MediaFile { MovieId = belowCutoff.Id, Path = "/movies/13/m.mkv", Quality = new QualityModel(lower, new Revision()) });
        database.Insert(new TrackedDownload { InfoHash = "AB", MovieId = downloading.Id, ReleaseTitle = "x", State = TrackedDownloadState.Downloading });
        using (ModularrDbContext db = database.NewContext())
        {
            db.Episodes.Single(e => e.SeriesId == complete.Id).MediaFileId = episodeFile.Id;
            db.SaveChanges();
        }

        using ModularrDbContext check = database.NewContext();
        List<SearchJob> wanted = await ScheduledSearch.WantedAsync(check, Today, TestContext.Current.CancellationToken);

        Assert.Equal([new SearchJob(missing.Id, null), new SearchJob(null, released.Id), new SearchJob(null, belowCutoff.Id)], wanted);
    }

    [Fact]
    public void EachCycleTakesTheCapFromAfterTheLastTitleAndWrapsAround()
    {
        SearchJob[] wanted = [new(1, null), new(2, null), new(null, 1), new(null, 2), new(null, 3)];

        IReadOnlyList<SearchJob> first = ScheduledSearch.Pick(wanted, after: null, cap: 2);
        IReadOnlyList<SearchJob> second = ScheduledSearch.Pick(wanted, first[^1], cap: 2);
        IReadOnlyList<SearchJob> third = ScheduledSearch.Pick(wanted, second[^1], cap: 2);

        Assert.Equal([new(1, null), new(2, null)], first);
        Assert.Equal([new(null, 1), new(null, 2)], second);
        Assert.Equal([new(null, 3), new(1, null)], third);
        // The last title is no longer wanted (searched and found): the turn goes on from the next one.
        Assert.Equal([new(null, 2), new(null, 3)], ScheduledSearch.Pick([.. wanted.Where(j => j.MovieId != 1)], new SearchJob(null, 1), cap: 2));
        Assert.Equal(wanted, ScheduledSearch.Pick(wanted, new SearchJob(null, 3), cap: 20));
    }

    private static (Quality Cutoff, Quality Lower) Qualities(TestDatabase database)
    {
        using ModularrDbContext db = database.NewContext();
        QualityProfile profile = db.QualityProfiles.Single(p => p.Id == 1);
        Quality[] qualities = [.. profile.Items.Select(i => i.Quality ?? i.Items[0].Quality!)];
        return (qualities.First(q => profile.Compare(q.Id, profile.Cutoff) >= 0), qualities.First(q => profile.Compare(q.Id, profile.Cutoff) < 0));
    }

    private static Series Show(int tvdbId, bool monitored, DateOnly airDate)
    {
        Series series = new() { TvdbId = tvdbId, Title = $"S{tvdbId}", SortTitle = $"s{tvdbId}", Path = $"/tv/{tvdbId}", QualityProfileId = 1, Monitored = monitored };
        series.Seasons.Add(new Season { Number = 1, Monitored = true });
        series.Episodes.Add(new Episode { SeasonNumber = 1, EpisodeNumber = 1, AirDate = airDate, Monitored = true });
        return series;
    }

    private static Movie Film(int tmdbId, DateOnly inCinemas) =>
        new() { TmdbId = tmdbId, Title = $"M{tmdbId}", Path = $"/movies/{tmdbId}", QualityProfileId = 1, Monitored = true, InCinemas = inCinemas };
}
