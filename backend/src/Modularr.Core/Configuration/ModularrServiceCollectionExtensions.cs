using System.Collections.Frozen;
using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
using Modularr.Core.Notifications;
using Modularr.Core.Persistence;
using Modularr.Core.PostProcessing;
using Modularr.Core.Search;
using Modularr.Core.Subtitles;

namespace Modularr.Core.Configuration;

public static class ModularrServiceCollectionExtensions
{
    // Indexer pages and .torrent files come from crowd-sourced public sites: a hostile or broken one must not exhaust memory.
    private const long MaxResponseBytes = 10 * 1024 * 1024;

    // Subtitle provider answers: a search page or one subtitle (archive), far below this.
    private const long MaxSubtitleResponseBytes = 4 * 1024 * 1024;

    // Discovery answers: the anime mapping file is the largest (a few MB), a TVmaze index page about 1 MB.
    private const long MaxDiscoveryResponseBytes = 32 * 1024 * 1024;
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(30);

    // TVmaze allows 20 requests per 10 s.
    private static readonly TimeSpan ShowIndexPageDelay = TimeSpan.FromMilliseconds(600);

    // Binds <configDirectory>/modularr.json to ModularrOptions (validated at startup) and registers the module services.
    public static IServiceCollection AddModularrCore(this IServiceCollection services, string configDirectory)
    {
        configDirectory = Path.GetFullPath(configDirectory);
        Directory.CreateDirectory(configDirectory);
        SettingsFile.MigrateLanguagePolicy(configDirectory);
        SettingsFile.RemoveVisularrSection(configDirectory);

        IConfigurationRoot file = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(configDirectory, ModularrOptions.FileName), optional: true, reloadOnChange: false)
            .Build();

        services.AddOptions<ModularrOptions>()
            .Bind(file, binder => binder.ErrorOnUnknownConfiguration = true)
            .PostConfigure(options => Resolve(options, file, configDirectory))
            .Validate(options => options.Auth.HasValidEnvironmentKey,
                $"{AuthOptions.ApiKeyVariable} must be at least 32 characters among letters, digits, '_' and '-'.")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton(new SettingsFile(configDirectory, file));

        services.AddDbContext<ModularrDbContext>((provider, builder) => builder.UseSqlite(
            new SqliteConnectionStringBuilder { DataSource = provider.GetRequiredService<IOptions<ModularrOptions>>().Value.Database.Path }.ToString()));
        services.AddSingleton(new DatabaseBackups(configDirectory));
        services.AddHostedService<DatabaseMigrator>();
        services.AddHostedService<DatabaseBackupScheduler>();
        // The torrent engine stops every torrent and writes its fast-resume data on shutdown: the default 30 s is not always enough.
        services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(60));

        services.AddSingleton<TorrentEngineHost>();
        services.AddHostedService(provider => provider.GetRequiredService<TorrentEngineHost>());
        // Fixed GitHub host: no redirects followed, a small cap, so the list download cannot be steered or grow.
        services.AddHttpClient(nameof(PublicTrackers), client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.MaxResponseContentBufferSize = PublicTrackers.MaxListBytes;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectCallback = PublicAddress.ConnectAsync });
        services.AddHostedService<PublicTrackers>();
        // Fixed what-is-my-IP hosts: same guard, a tiny answer, a short wait so a dead route shows up as an error quickly.
        services.AddHttpClient(nameof(PublicIpMonitor), client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.MaxResponseContentBufferSize = PublicIpMonitor.MaxResponseBytes;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectCallback = PublicAddress.ConnectAsync });
        services.AddSingleton<PublicIpMonitor>();
        services.AddHostedService(provider => provider.GetRequiredService<PublicIpMonitor>());

        services.AddHttpClient();
        // Indexer links and their redirects are remote data: every hop may only connect to a public address (no SSRF).
        services.AddHttpClient(nameof(DownloadService), client => client.MaxResponseContentBufferSize = MaxResponseBytes)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { MaxAutomaticRedirections = 5, ConnectCallback = PublicAddress.ConnectAsync });
        services.AddSingleton<IndexerCatalogHost>();
        services.AddHostedService(provider => provider.GetRequiredService<IndexerCatalogHost>());
        services.AddSingleton<IndexerDomainHealth>();
        services.AddSingleton<CardigannSessions>();

        // Redirects are handled per search path (Cardigann "followredirect"), so the handler must not follow them itself.
        services.AddHttpClient<CardigannSearcher>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Modularr/1.0");
                client.MaxResponseContentBufferSize = MaxResponseBytes;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                // Definitions, redirects and selector links come from remote data: never reach the local network.
                ConnectCallback = PublicAddress.ConnectAsync,
            });

        // Redirects are followed by hand, and only to the provider's own HTTPS hosts (OpenSubtitlesProvider.SendAsync).
        services.AddHttpClient<OpenSubtitlesProvider>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.MaxResponseContentBufferSize = MaxSubtitleResponseBytes;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All });
        services.AddTransient<ISubtitleProvider>(provider => provider.GetRequiredService<OpenSubtitlesProvider>());

        // Admin-chosen targets, often on the LAN, so no PublicAddress guard; no redirects, so the token header is not forwarded elsewhere.
        services.AddHttpClient<NotificationSender>(client => client.Timeout = NotificationSender.Timeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddHostedService<NotificationService>();
        services.AddHttpClient<SeriesMetadataClient>();
        services.AddHttpClient<MovieMetadataClient>();
        services.AddHttpClient<ShowDiscoveryClient>(client =>
        {
            client.Timeout = DiscoveryTimeout;
            client.MaxResponseContentBufferSize = MaxDiscoveryResponseBytes;
        });
        services.AddHttpClient<TmdbClient>(client =>
        {
            client.Timeout = DiscoveryTimeout;
            client.MaxResponseContentBufferSize = MaxDiscoveryResponseBytes;
        });
        services.AddSingleton(provider => new ShowIndex(
            (page, ct) => provider.GetRequiredService<ShowDiscoveryClient>().GetShowIndexPageAsync(page, ct),
            ShowIndexPageDelay,
            provider.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));
        services.AddMemoryCache(cache => cache.SizeLimit = 1000);
        // Lookups and explore pages, keyed by what members type, get their own cache: filling it cannot push out the dashboard
        // coverage or the OpenSubtitles login token kept in the shared one.
        services.AddKeyedSingleton<IMemoryCache>(DiscoveryService.CacheKey, (_, _) => new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }));

        // The end-to-end flow: library, search, grab, then the monitor imports finished torrents.
        services.AddTransient<IndexerSearch>();
        services.AddScoped<AccountService>();
        services.AddScoped<LibraryService>();
        services.AddScoped<CollectionService>();
        services.AddScoped<DiscoveryService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<CalendarService>();
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
        services.AddScoped<SubtitleService>();
        services.AddSingleton<SubtitleRunner>();
        services.AddHostedService(provider => provider.GetRequiredService<SubtitleRunner>());
        services.AddHostedService<MetadataBackfill>();
        services.AddHostedService<MetadataRefresher>();
        services.AddHostedService<ScheduledSearch>();

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
        options.Metadata = options.Metadata with { EnvironmentTmdbApiKey = Environment.GetEnvironmentVariable(MetadataOptions.TmdbApiKeyVariable) };
        options.Auth = options.Auth.WithEnvironmentKey(Environment.GetEnvironmentVariable(AuthOptions.ApiKeyVariable));

        // The binder appends configured items to a non-empty default set (case-sensitively); a configured list must replace it.
        IConfigurationSection postProcessing = file.GetSection(nameof(ModularrOptions.PostProcessing));
        options.PostProcessing = options.PostProcessing with
        {
            VideoExtensions = ReadSet(postProcessing, nameof(PostProcessingOptions.VideoExtensions)) ?? options.PostProcessing.VideoExtensions,
            SubtitleExtensions = ReadSet(postProcessing, nameof(PostProcessingOptions.SubtitleExtensions)) ?? options.PostProcessing.SubtitleExtensions,
            IgnorableExtensions = ReadSet(postProcessing, nameof(PostProcessingOptions.IgnorableExtensions)) ?? options.PostProcessing.IgnorableExtensions,
            RecycleBinPath = ResolvePath(options.PostProcessing.RecycleBinPath, configDirectory),
        };
    }

    // An empty value is left for [Required] to report.
    private static string ResolvePath(string path, string configDirectory) =>
        string.IsNullOrWhiteSpace(path) ? path : Path.GetFullPath(path, configDirectory);

    private static FrozenSet<string>? ReadSet(IConfigurationSection section, string key) =>
        section.GetSection(key).Get<string[]>()?.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}
