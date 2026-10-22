using System.Text;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Languages;
using Modularr.Core.Library;
using Modularr.Core.Parsing;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;
using Modularr.Core.Subtitles;
using Modularr.Core.Tests.Library;
using Modularr.Core.Tests.Persistence;
using Modularr.Core.Tests.PostProcessing;

namespace Modularr.Core.Tests.Subtitles;

public sealed class SubtitleServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly TempDownload _library = new();
    private readonly FakeProvider _provider = new();

    public void Dispose()
    {
        _database.Dispose();
        _library.Dispose();
    }

    private ModularrOptions Settings(bool onlyWhenAudioDiffers = true) => new()
    {
        Paths = new PathOptions { Movies = _library.PathOf("movies"), Series = _library.PathOf("tv"), Anime = _library.PathOf("anime") },
        Media = new MediaOptions { Movies = new MediaDefaults { Subtitles = [Language.French, Language.English] } },
        Subtitles = new SubtitleOptions { OnlyWhenAudioDiffers = onlyWhenAudioDiffers },
    };

    private SubtitleService Service(ModularrDbContext db, ModularrOptions? settings = null) =>
        new(db, new ActivityLog(db, new ActivityFeed()), new FixedOptionsMonitor(settings ?? Settings()), [_provider]);

    // A Japanese movie whose file is the release "<release>.mkv"; its audio comes from the release title.
    private (Movie Movie, MediaFile File) Imported(string release)
    {
        Movie movie = _database.Insert(new Movie
        {
            TmdbId = 27205, ImdbId = "tt1375666", Title = "Inception", Year = 2010, Path = _library.PathOf("movies/Inception (2010)"),
            QualityProfileId = 1, OriginalLanguage = Language.Japanese,
        })[0];
        string path = _library.CreateFile($"movies/Inception (2010)/{release}.mkv");
        MediaFile file = _database.Insert(new MediaFile
        {
            MovieId = movie.Id, Path = path, Quality = new QualityModel(Quality.Bluray1080p, new Revision()), Languages = LanguageParser.ParseLanguages(release),
        })[0];
        return (movie, file);
    }

    [Fact]
    public async Task AfterImportTheBestCandidateOfEachMissingLanguageIsSaved()
    {
        (Movie movie, MediaFile file) = Imported("Inception.2010.1080p.BluRay.x264-GRP");
        _provider.Offer("1", Language.French, "Other.Release", downloads: 9000);
        _provider.Offer("2", Language.French, "Inception.2010.1080p.BluRay.x264-GRP", downloads: 10);
        _provider.Offer("3", Language.French, "Inception.2010.1080p.BluRay.x264-GRP.forced", forced: true, hashMatch: true);
        using ModularrDbContext db = _database.NewContext();

        Assert.True(await Service(db).DownloadMissingAsync(file.Id, reportMissing: true, TestContext.Current.CancellationToken));

        Assert.Equal(["2"], _provider.Downloaded);
        Assert.True(File.Exists(Path.Combine(movie.Path, "Inception.2010.1080p.BluRay.x264-GRP.fr.srt")));
        Assert.Equal(
            [(ActivityKind.SubtitleDownloaded, "French subtitles saved as Inception.2010.1080p.BluRay.x264-GRP.fr.srt"), (ActivityKind.SubtitlesMissing, "No English subtitles found")],
            db.Activities.OrderBy(a => a.Id).Select(a => new { a.Kind, a.Message }).AsEnumerable().Select(a => (a.Kind, a.Message)));
        Assert.NotNull(db.MediaFiles.Single().SubtitlesSearched);

        SubtitleStatus status = await Service(db).StatusAsync(movie.Id, null, TestContext.Current.CancellationToken);
        Assert.Equal([Language.French], status.Present.Select(p => p.Language));
        Assert.Equal([Language.English], status.Missing);
    }

    [Fact]
    public async Task ALanguageTheAudioIsInIsNotWantedWhenOnlyForeignAudioNeedsSubtitles()
    {
        (Movie movie, MediaFile _) = Imported("Inception.2010.MULTi.1080p.BluRay.x264-GRP");
        using ModularrDbContext db = _database.NewContext();

        // MULTi: French and the original Japanese.
        Assert.Equal([Language.English], (await Service(db).StatusAsync(movie.Id, null, TestContext.Current.CancellationToken)).Missing);
        Assert.Equal(
            [Language.French, Language.English],
            (await Service(db, Settings(onlyWhenAudioDiffers: false)).StatusAsync(movie.Id, null, TestContext.Current.CancellationToken)).Missing);
    }

    [Fact]
    public async Task AManualDownloadNeverReplacesASubtitle()
    {
        (Movie movie, MediaFile _) = Imported("Inception.2010.1080p.BluRay.x264-GRP");
        File.WriteAllText(Path.Combine(movie.Path, "Inception.2010.1080p.BluRay.x264-GRP.fr.srt"), "mine");
        using ModularrDbContext db = _database.NewContext();
        SubtitleCandidate candidate = new(FakeProvider.ProviderName, "9", Language.French, "", false, false, false, 0);

        await Assert.ThrowsAsync<ArgumentException>(() => Service(db).DownloadAsync(movie.Id, null, candidate, TestContext.Current.CancellationToken));

        Assert.Equal("mine", File.ReadAllText(Path.Combine(movie.Path, "Inception.2010.1080p.BluRay.x264-GRP.fr.srt")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(db).DownloadAsync(movie.Id, null, candidate with { Provider = "Elsewhere" }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheRetrySkipsFilesSearchedRecentlyAndStopsWithoutProviders()
    {
        (Movie _, MediaFile file) = Imported("Inception.2010.1080p.BluRay.x264-GRP");
        using ModularrDbContext db = _database.NewContext();
        DateTime now = DateTime.UtcNow;

        Assert.Equal(1, await Service(db).RetryMissingAsync(now, TestContext.Current.CancellationToken));
        Assert.Equal(0, await Service(db).RetryMissingAsync(now, TestContext.Current.CancellationToken));
        Assert.Equal(1, await Service(db).RetryMissingAsync(now + SubtitleService.RetryInterval + TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        _provider.Enabled = false;
        Assert.Equal(0, await Service(db).RetryMissingAsync(now.AddDays(1), TestContext.Current.CancellationToken));
        Assert.Empty(db.Activities); // the retry does not repeat SubtitlesMissing
        Assert.Equal(file.Id, db.MediaFiles.Single().Id);
    }

    private sealed class FakeProvider : ISubtitleProvider
    {
        public const string ProviderName = "Fake";

        private readonly List<SubtitleCandidate> _offers = [];

        public List<string> Downloaded { get; } = [];

        public string Name => ProviderName;

        public bool Enabled { get; set; } = true;

        public void Offer(string id, Language language, string release, int downloads = 0, bool forced = false, bool hashMatch = false) =>
            _offers.Add(new SubtitleCandidate(ProviderName, id, language, release, false, forced, hashMatch, downloads));

        public Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(SubtitleQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubtitleCandidate>>([.. _offers.Where(o => query.Languages.Contains(o.Language))]);

        public Task<SubtitleDownload> DownloadAsync(string id, CancellationToken cancellationToken)
        {
            Downloaded.Add(id);
            return Task.FromResult(new SubtitleDownload($"{id}.srt", Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nBonjour\n")));
        }
    }
}
