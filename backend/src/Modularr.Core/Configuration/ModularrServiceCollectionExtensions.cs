using System.Collections.Frozen;
using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modularr.Core.Accounts;
using Modularr.Core.Activities;
using Modularr.Core.Discovery;
using Modularr.Core.Downloads;
using Modularr.Core.Downloads.Torrents;
using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Cardigann;
using Modularr.Core.Indexers.Catalog;
using Modularr.Core.Library;
using Modularr.Core.Metadata;
using Modularr.Core.Persistence;
using Modularr.Core.PostProcessing;
using Modularr.Core.Search;

namespace Modularr.Core.Configuration;

public static class ModularrServiceCollectionExtensions
{
    // Indexer pages and .torrent files come from crowd-sourced public sites: a hostile or broken one must not exhaust memory.
    private const long MaxResponseBytes = 10 * 1024 * 1024;

    // Binds <configDirectory>/modularr.json to ModularrOptions (validated at startup) and registers the module services.
    public static IServiceCollection AddModularrCore(this IServiceCollection services, string configDirectory)
    {
        configDirectory = Path.GetFullPath(configDirectory);
        Directory.CreateDirectory(configDirectory);

        IConfigurationRoot file = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(configDirectory, ModularrOptions.FileName), optional: true, reloadOnChange: false)
            .Build();

        services.AddOptions<ModularrOptions>()
            .Bind(file, binder => binder.ErrorOnUnknownConfiguration = true)
            .PostConfigure(options => Resolve(options, file, configDirectory))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton(new SettingsFile(configDirectory, file));

        services.AddDbContext<ModularrDbContext>((provider, builder) => builder.UseSqlite(
            new SqliteConnectionStringBuilder { DataSource = provider.GetRequiredService<IOptions<ModularrOptions>>().Value.Database.Path }.ToString()));
        services.AddHostedService<DatabaseMigrator>();

        services.AddSingleton<TorrentEngineHost>();
        services.AddHostedService(provider => provider.GetRequiredService<TorrentEngineHost>());

        services.AddHttpClient();
        services.AddHttpClient(nameof(DownloadService), client => client.MaxResponseContentBufferSize = MaxResponseBytes);
        services.AddSingleton<IndexerCatalogHost>();
        services.AddHostedService(provider => provider.GetRequiredService<IndexerCatalogHost>());
        services.AddSingleton<IndexerDomainHealth>();

        // Redirects are handled per search path (Cardigann "followredirect"), so the handler must not follow them itself.
        services.AddHttpClient<CardigannSearcher>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Modularr/1.0");
                client.MaxResponseContentBufferSize = MaxResponseBytes;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All });

        services.AddHttpClient<SeriesMetadataClient>();
        services.AddHttpClient<MovieMetadataClient>();
        services.AddHttpClient<ShowDiscoveryClient>();
        services.AddMemoryCache(cache => cache.SizeLimit = 1000);

        // The end-to-end flow: library, search, grab, then the monitor imports finished torrents.
        services.AddTransient<IndexerSearch>();
        services.AddScoped<AccountService>();
        services.AddScoped<LibraryService>();
        services.AddScoped<DiscoveryService>();
        services.AddScoped<ReleaseSearch>();
        services.AddScoped<DownloadService>();
        services.AddScoped<DownloadImporter>();
        services.AddScoped<FailedDownloadService>();
        services.AddScoped<QualityProfileService>();
        services.AddScoped<AutoSearch>();
        services.AddScoped<DiskScanService>();
        services.AddScoped<LibraryImportService>();
        services.AddHostedService<LibraryScanner>();
        services.AddSingleton<ActivityFeed>();
        services.AddScoped<ActivityLog>();
        services.AddHostedService<ActivityPruner>();
        services.AddHostedService<DownloadProgressTicker>();
        services.AddSingleton<SearchRunner>();
        services.AddHostedService(provider => provider.GetRequiredService<SearchRunner>());
        services.AddHostedService<DownloadMonitor>();
        services.AddHostedService<MetadataBackfill>();

        return services;
    }

    private static void Resolve(ModularrOptions options, IConfiguration file, string configDirectory)
    {
        options.Paths = options.Paths with
        {
            Series = ResolvePath(options.Paths.Series, configDirectory),
            Movies = ResolvePath(options.Paths.Movies, configDirectory),
            Anime = ResolvePath(options.Paths.Anime, configDirectory),
            Downloads = ResolvePath(options.Paths.Downloads, configDirectory),
        };
        options.Torrents = options.Torrents with { StateDirectory = ResolvePath(options.Torrents.StateDirectory, configDirectory) };
        options.Indexers = options.Indexers with { DefinitionsDirectory = ResolvePath(options.Indexers.DefinitionsDirectory, configDirectory) };
        options.Database = options.Database with { Path = ResolvePath(options.Database.Path, configDirectory) };

        // The binder appends configured items to a non-empty default set (case-sensitively); a configured list must replace it.
        IConfigurationSection postProcessing = file.GetSection(nameof(ModularrOptions.PostProcessing));
        options.PostProcessing = options.PostProcessing with
        {
            VideoExtensions = ReadSet(postProcessing, nameof(PostProcessingOptions.VideoExtensions)) ?? options.PostProcessing.VideoExtensions,
            SubtitleExtensions = ReadSet(postProcessing, nameof(PostProcessingOptions.SubtitleExtensions)) ?? options.PostProcessing.SubtitleExtensions,
            IgnorableExtensions = ReadSet(postProcessing, nameof(PostProcessingOptions.IgnorableExtensions)) ?? options.PostProcessing.IgnorableExtensions,
        };
    }

    // An empty value is left for [Required] to report.
    private static string ResolvePath(string path, string configDirectory) =>
        string.IsNullOrWhiteSpace(path) ? path : Path.GetFullPath(path, configDirectory);

    private static FrozenSet<string>? ReadSet(IConfigurationSection section, string key) =>
        section.GetSection(key).Get<string[]>()?.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}
