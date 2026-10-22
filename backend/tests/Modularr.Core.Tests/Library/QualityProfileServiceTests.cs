using Microsoft.Extensions.Configuration;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Library;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;
using Modularr.Core.Tests.Persistence;

namespace Modularr.Core.Tests.Library;

public sealed class QualityProfileServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly string _configDirectory = Directory.CreateTempSubdirectory("modularr-profiles-").FullName;

    public void Dispose()
    {
        _database.Dispose();
        Directory.Delete(_configDirectory, recursive: true);
    }

    // HD-1080p: cutoff Bluray-1080p, the WEB 1080p group allowed.
    private static QualityProfile Hd1080p => DefaultQualityProfiles.Create()[3] with { Id = 0, Name = "Mine" };

    [Fact]
    public void TheSeededProfilesAreValid()
    {
        foreach (QualityProfile profile in DefaultQualityProfiles.Create())
        {
            QualityProfileService.Validate(profile);
        }
    }

    [Fact]
    public void ACutoffOnAGroupIsValid()
    {
        QualityProfile profile = Hd1080p;
        QualityProfileService.Validate(profile with { Cutoff = profile.Items.Single(i => i.Quality is null && i.Allowed).Id });
    }

    public static TheoryData<string, Func<QualityProfile, QualityProfile>> InvalidProfiles => new()
    {
        { "no name", p => p with { Name = " " } },
        { "nothing allowed", p => p with { Items = [.. p.Items.Select(i => i with { Allowed = false })] } },
        { "cutoff not allowed", p => p with { Cutoff = Quality.Sdtv.Id } },
        { "cutoff unknown", p => p with { Cutoff = 999 } },
        { "quality missing", p => p with { Items = [.. p.Items.Where(i => i.Quality != Quality.Cam)] } },
        { "quality twice", p => p with { Items = [.. p.Items, new QualityProfileQualityItem { Quality = Quality.Cam }] } },
        { "group without id", p => p with { Items = [.. p.Items.Select(i => i.Quality is null ? i with { Id = 0 } : i)] } },
        { "empty group", p => p with { Items = [.. p.Items, new QualityProfileQualityItem { Id = 2000, Name = "Empty" }] } },
    };

    [Theory]
    [MemberData(nameof(InvalidProfiles))]
    public void InvalidProfilesAreRejected(string why, Func<QualityProfile, QualityProfile> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Assert.Throws<ArgumentException>(() => QualityProfileService.Validate(change(Hd1080p)));
        Assert.NotEmpty(why);
    }

    [Fact]
    public async Task CreatesUpdatesAndDeletesAnUnusedProfile()
    {
        using ModularrDbContext db = _database.NewContext();
        QualityProfileService profiles = Service(db);
        CancellationToken ct = TestContext.Current.CancellationToken;

        QualityProfile created = await profiles.CreateAsync(Hd1080p, ct);
        QualityProfile updated = await profiles.UpdateAsync(created.Id, created with { Name = "Renamed", UpgradeAllowed = true }, ct);
        await Assert.ThrowsAsync<ArgumentException>(() => profiles.CreateAsync(Hd1080p with { Name = "Renamed" }, ct));
        await profiles.DeleteAsync(created.Id, ct);

        Assert.Equal(7, created.Id);
        Assert.Equal(("Renamed", true), (updated.Name, updated.UpgradeAllowed));
        Assert.Equal(6, (await profiles.ListAsync(ct)).Count);
    }

    [Fact]
    public async Task AProfileInUseIsNotDeleted()
    {
        Movie movie = _database.AddMovie(603);
        using ModularrDbContext db = _database.NewContext();
        MediaDefaults hd = new() { ProfileId = 4 };
        QualityProfileService profiles = Service(db, new MediaOptions { Movies = hd, Series = hd, Anime = hd });
        CancellationToken ct = TestContext.Current.CancellationToken;

        ArgumentException used = await Assert.ThrowsAsync<ArgumentException>(() => profiles.DeleteAsync(movie.QualityProfileId, ct));
        ArgumentException isDefault = await Assert.ThrowsAsync<ArgumentException>(() => profiles.DeleteAsync(4, ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => profiles.DeleteAsync(99, ct));

        Assert.Contains("used by the library", used.Message, StringComparison.Ordinal);
        Assert.Contains("default", isDefault.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MediaSettingsNeedExistingProfiles()
    {
        using ModularrDbContext db = _database.NewContext();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(db).SaveMediaAsync(new MediaOptions { Anime = new MediaDefaults { ProfileId = 42 } }, TestContext.Current.CancellationToken));

        Assert.False(File.Exists(Path.Combine(_configDirectory, ModularrOptions.FileName)));
    }

    private QualityProfileService Service(ModularrDbContext db, MediaOptions? media = null) =>
        new(db, new SettingsFile(_configDirectory, new ConfigurationBuilder().Build()), new FixedOptionsMonitor(new ModularrOptions { Media = media ?? new MediaOptions() }));
}
