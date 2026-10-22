using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Library;
using Modularr.Core.Metadata;
using Modularr.Core.Persistence;
using Modularr.Core.Search;
using Modularr.Core.Tests.Discovery;
using Modularr.Core.Tests.Metadata;
using Modularr.Core.Tests.Persistence;
using Modularr.Core.Tests.PostProcessing;

namespace Modularr.Core.Tests.Library;

public sealed class LibraryImportServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly TempDownload _disk = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private readonly StubHttpHandler _http = new StubHttpHandler()
        .Fixture("https://api.radarr.video/v1/search?", "radarr-search-prometheus.json")
        .Fixture("https://api.radarr.video/v1/movie/70981", "radarr-movie-70981.json");

    public void Dispose()
    {
        _cache.Dispose();
        _database.Dispose();
        _disk.Dispose();
    }

    private string Full(string relative) => Path.GetFullPath(_disk.PathOf(relative));

    [Theory]
    [InlineData("Inception (2010)", "Inception", 2010, null, null)]
    [InlineData("The Office (US) [tvdbid-73244]", "The Office (US)", null, "tvdb", "73244")]
    [InlineData("Prometheus (2012) {tmdb-70981}", "Prometheus", 2012, "tmdb", "70981")]
    [InlineData("Heat {imdb-tt0113277}", "Heat", null, "imdb", "tt0113277")]
    [InlineData("Blade Runner 2049", "Blade Runner 2049", null, null, null)]
    public void FolderNamesAreParsed(string name, string title, int? year, string? source, string? id) =>
        Assert.Equal(new LibraryFolderName(title, year, source, id), LibraryFolderName.Parse(name));

    [Fact]
    public async Task UnownedFoldersGetAMatchWithItsConfidence()
    {
        Directory.CreateDirectory(Full("movies/Prometheus (2012)"));
        Directory.CreateDirectory(Full("movies/Owned"));
        _database.Insert(new Movie { TmdbId = 1, Title = "Owned", Path = Full("movies/Owned"), QualityProfileId = 1 });

        using ModularrDbContext db = _database.NewContext();
        ImportProposals proposals = await Service(db).ProposeAsync(LibraryRoot.Movies, TestContext.Current.CancellationToken);

        ImportProposal proposal = Assert.Single(proposals.Items);
        Assert.Equal((1, Full("movies/Prometheus (2012)")), (proposals.Total, proposal.Folder));
        Assert.Equal((70981, MatchConfidence.High), (proposal.Match!.TmdbId!.Value, proposal.Confidence));
    }

    [Fact]
    public async Task AFolderIsImportedWithItsFilesAndNotSearched()
    {
        string folder = Full("movies/Prometheus (2012)");
        string video = Path.GetFullPath(_disk.CreateFile("movies/Prometheus (2012)/Prometheus.2012.1080p.BluRay.x264-GRP.mkv"));

        using ModularrDbContext db = _database.NewContext();
        ImportOutcome outcome = Assert.Single(await Service(db).ImportAsync([new ImportItem(folder, TmdbId: 70981)], TestContext.Current.CancellationToken));

        Assert.Equal((folder, 1, (string?)null), (outcome.Folder, outcome.Files, outcome.Error));
        using ModularrDbContext check = _database.NewContext();
        Movie movie = check.Movies.Single();
        Assert.Equal((outcome.MovieId!.Value, folder), (movie.Id, movie.Path));
        Assert.Equal(video, check.MediaFiles.Single().Path);
        Assert.Equal([ActivityKind.Added, ActivityKind.Imported], check.Activities.OrderBy(a => a.Id).Select(a => a.Kind));
    }

    [Fact]
    public async Task InvalidFoldersAreRejectedOneByOne()
    {
        Directory.CreateDirectory(Full("movies/Owned"));
        Directory.CreateDirectory(Full("elsewhere/Prometheus"));
        _database.Insert(new Movie { TmdbId = 1, Title = "Owned", Path = Full("movies/Owned"), QualityProfileId = 1 });
        ImportItem[] items =
        [
            new(Full("elsewhere/Prometheus"), TmdbId: 70981),
            new(Path.Combine(_disk.Root, "movies", "..", "elsewhere", "Prometheus"), TmdbId: 70981),
            new(Full("movies/Owned"), TmdbId: 70981),
            new(Full("series/Prometheus"), TmdbId: 70981),
            new("movies/Prometheus", TmdbId: 70981),
            new(Full("movies/Owned"), TmdbId: 70981, TvdbId: 1),
        ];

        using ModularrDbContext db = _database.NewContext();
        IReadOnlyList<ImportOutcome> outcomes = await Service(db).ImportAsync(items, TestContext.Current.CancellationToken);

        Assert.All(outcomes, o => Assert.NotNull(o.Error));
        Assert.Contains("already belongs", outcomes[2].Error, StringComparison.Ordinal);
        Assert.Contains("not a folder of the movies root", outcomes[0].Error, StringComparison.Ordinal);
        Assert.Single(_database.NewContext().Movies);
        Assert.Empty(_http.Requests);
    }

    private LibraryImportService Service(ModularrDbContext db)
    {
        IOptions<ModularrOptions> metadataOptions = Options.Create(new ModularrOptions());
        HttpClient client = new(_http);
        FixedOptionsMonitor settings = new(new ModularrOptions
        {
            Paths = new PathOptions { Series = Full("series"), Movies = Full("movies"), Anime = Full("anime") },
        });
        ActivityLog activities = new(db, new ActivityFeed());
        MovieMetadataClient movies = new(client, metadataOptions);
        SeriesMetadataClient series = new(client, metadataOptions);
        return new LibraryImportService(
            db,
            DiscoveryServiceTests.Create(client, _cache, db),
            new LibraryService(db, series, movies, settings, new SearchRunner(null!), activities),
            new DiskScanService(db, activities, settings),
            activities,
            settings);
    }
}
