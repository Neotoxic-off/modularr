using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modularr.Core.Decisions;
using Modularr.Core.Languages;
using Modularr.Core.Qualities;

namespace Modularr.Core.Persistence;

// The single <config dir>/modularr.db. Rows of a deleted series or movie go with it (upstream deletes them in event handlers).
public sealed class ModularrDbContext(DbContextOptions<ModularrDbContext> options) : DbContext(options)
{
    private const string NoCase = "NOCASE";

    public DbSet<Series> Series => Set<Series>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<QualityProfile> QualityProfiles => Set<QualityProfile>();
    public DbSet<BlocklistItem> Blocklist => Set<BlocklistItem>();
    public DbSet<HistoryItem> History => Set<HistoryItem>();
    public DbSet<TrackedDownload> TrackedDownloads => Set<TrackedDownload>();
    public DbSet<User> Users => Set<User>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<Quality>().HaveConversion<QualityIdConverter>();
        configurationBuilder.Properties<QualityModel>().HaveConversion<JsonValueConverter<QualityModel>, JsonValueComparer<QualityModel>>();
        configurationBuilder.Properties<IReadOnlyList<Language>>()
            .HaveConversion<JsonValueConverter<IReadOnlyList<Language>>, JsonValueComparer<IReadOnlyList<Language>>>();
        configurationBuilder.Properties<Language>().HaveConversion<LanguageIdConverter>();
        configurationBuilder.Properties<IReadOnlyList<QualityProfileQualityItem>>()
            .HaveConversion<JsonValueConverter<IReadOnlyList<QualityProfileQualityItem>>, JsonValueComparer<IReadOnlyList<QualityProfileQualityItem>>>();
        configurationBuilder.Properties<Dictionary<string, string>>()
            .HaveConversion<JsonValueConverter<Dictionary<string, string>>, JsonValueComparer<Dictionary<string, string>>>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QualityProfile>(profile =>
        {
            profile.HasIndex(p => p.Name).IsUnique();
            profile.HasData(DefaultQualityProfiles.Create());
        });

        modelBuilder.Entity<Series>(series =>
        {
            series.HasIndex(s => s.TvdbId).IsUnique();
            series.OwnsMany(s => s.Seasons, seasons => seasons.ToJson());
            series.HasOne(s => s.QualityProfile).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Episode>(episode =>
        {
            episode.HasIndex(e => new { e.SeriesId, e.SeasonNumber, e.EpisodeNumber }).IsUnique();
            episode.HasOne(e => e.Series).WithMany(s => s.Episodes).OnDelete(DeleteBehavior.Cascade);
            episode.HasOne<MediaFile>().WithMany().HasForeignKey(e => e.MediaFileId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Movie>(movie =>
        {
            movie.HasIndex(m => m.TmdbId).IsUnique();
            movie.HasOne(m => m.QualityProfile).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MediaFile>(file =>
        {
            file.HasIndex(f => f.Path).IsUnique();
            BelongsToSeriesOrMovie(file, f => f.SeriesId, f => f.MovieId);
        });

        modelBuilder.Entity<BlocklistItem>(blocklist =>
        {
            blocklist.Property(b => b.SourceTitle).UseCollation(NoCase);
            blocklist.Property(b => b.InfoHash).UseCollation(NoCase);
            blocklist.HasIndex(b => b.InfoHash).IsUnique();
            BelongsToSeriesOrMovie(blocklist, b => b.SeriesId, b => b.MovieId);
        });

        modelBuilder.Entity<HistoryItem>(history =>
        {
            history.Property(h => h.Data); // get-only, not mapped by convention
            history.Property(h => h.InfoHash).UseCollation(NoCase);
            history.HasIndex(h => h.InfoHash);
            history.HasOne<Episode>().WithMany().HasForeignKey(h => h.EpisodeId).OnDelete(DeleteBehavior.Cascade);
            BelongsToSeriesOrMovie(history, h => h.SeriesId, h => h.MovieId);
        });

        modelBuilder.Entity<TrackedDownload>(download =>
        {
            download.Property(d => d.InfoHash).UseCollation(NoCase);
            download.HasIndex(d => d.InfoHash).IsUnique();
            BelongsToSeriesOrMovie(download, d => d.SeriesId, d => d.MovieId);
        });

        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Username).UseCollation(NoCase);
            user.HasIndex(u => u.Username).IsUnique();
        });
    }

    // Exactly one owner, and the row is deleted with it.
    private static void BelongsToSeriesOrMovie<T>(
        EntityTypeBuilder<T> entity,
        Expression<Func<T, object?>> seriesId,
        Expression<Func<T, object?>> movieId)
        where T : class
    {
        entity.HasOne<Series>().WithMany().HasForeignKey(seriesId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Movie>().WithMany().HasForeignKey(movieId).OnDelete(DeleteBehavior.Cascade);
        entity.ToTable(table => table.HasCheckConstraint($"CK_{typeof(T).Name}_SeriesOrMovie", "(SeriesId IS NULL) <> (MovieId IS NULL)"));
    }
}
