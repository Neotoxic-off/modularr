using System.Text.Json;
using Modularr.Core.Library;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;
using Modularr.Core.Tests.Persistence;

namespace Modularr.Core.Tests.Library;

public sealed class CalendarServiceTests : IDisposable
{
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly DateOnly End = new(2026, 10, 31);

    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Theory]
    [InlineData(0, true)]
    [InlineData(61, true)]
    [InlineData(62, false)]
    [InlineData(-1, false)]
    public async Task TheRangeRunsForwardOverAtMost62Days(int days, bool valid)
    {
        using ModularrDbContext db = _database.NewContext();
        Func<Task<IReadOnlyList<CalendarEvent>>> events = () => new CalendarService(db).GetAsync(Start, Start.AddDays(days), TestContext.Current.CancellationToken);

        if (valid)
        {
            Assert.Empty(await events());
        }
        else
        {
            await Assert.ThrowsAsync<ArgumentException>(events);
        }
    }

    [Fact]
    public async Task MapsEpisodesAndEachMovieReleaseInTheRangeByDate()
    {
        Movie dune = _database.Insert(new Movie
        {
            TmdbId = 1, Title = "Dune", Path = "/movies/dune", QualityProfileId = 1, Monitored = true, Poster = "https://img/dune.jpg",
            InCinemas = new DateOnly(2026, 9, 1), DigitalRelease = new DateOnly(2026, 10, 20), PhysicalRelease = new DateOnly(2026, 10, 5),
        })[0];
        _database.Insert(new Movie { TmdbId = 2, Title = "Later", Path = "/movies/later", QualityProfileId = 1, InCinemas = new DateOnly(2026, 12, 1) });
        _database.Insert(new MediaFile { MovieId = dune.Id, Path = "/movies/dune/dune.mkv", Quality = new QualityModel(Quality.Bluray1080p, new Revision()) });
        Series anime = _database.Insert(new Series
        {
            TvdbId = 10, Title = "Frieren", SortTitle = "frieren", Path = "/anime/f", QualityProfileId = 1, Monitored = true, SeriesType = SeriesType.Anime, Poster = "https://img/f.jpg",
        })[0];
        _database.Insert(
            new Episode { SeriesId = anime.Id, SeasonNumber = 1, EpisodeNumber = 2, AirDate = new DateOnly(2026, 10, 12), Title = "Two", Monitored = true },
            new Episode { SeriesId = anime.Id, SeasonNumber = 1, EpisodeNumber = 1, AirDate = new DateOnly(2026, 9, 30), Title = "One", Monitored = true });

        using ModularrDbContext db = _database.NewContext();
        IReadOnlyList<CalendarEvent> events = await new CalendarService(db).GetAsync(Start, End, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new MovieEvent(new DateOnly(2026, 10, 5), dune.Id, "Dune", "physical", "https://img/dune.jpg", HasFile: true, Monitored: true),
                new EpisodeEvent(new DateOnly(2026, 10, 12), anime.Id, "Frieren", 1, 2, "Two", "https://img/f.jpg", "anime", HasFile: false, Monitored: true),
                new MovieEvent(new DateOnly(2026, 10, 20), dune.Id, "Dune", "digital", "https://img/dune.jpg", HasFile: true, Monitored: true),
            ],
            events);

        // The UI tells the two shapes apart by type.
        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(events, JsonSerializerOptions.Web));
        Assert.Equal(["movie", "episode", "movie"], json.RootElement.EnumerateArray().Select(e => e.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task UpcomingTakesTheNextTenEventsWithinSixMonths()
    {
        Series show = _database.AddSeries(1);
        _database.Insert([.. Enumerable.Range(1, 12).Select(i => new Episode { SeriesId = show.Id, SeasonNumber = 1, EpisodeNumber = i, AirDate = Start.AddDays(i * 10) })]);
        _database.Insert(new Movie { TmdbId = 1, Title = "Soon", Path = "/movies/soon", QualityProfileId = 1, DigitalRelease = Start.AddDays(5) });

        using ModularrDbContext db = _database.NewContext();
        IReadOnlyList<CalendarEvent> events = await new CalendarService(db).UpcomingAsync(Start, TestContext.Current.CancellationToken);

        Assert.Equal([Start.AddDays(5), .. Enumerable.Range(1, 9).Select(i => Start.AddDays(i * 10))], events.Select(e => e.Date));
    }
}
