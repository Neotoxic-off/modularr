using System.Collections.Frozen;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modularr.Core.Downloads.Torrents;
using Modularr.Core.Indexers.Cardigann;
using Modularr.Core.PostProcessing;

namespace Modularr.Core.Configuration;

public static class ModularrServiceCollectionExtensions
{
    // Binds <configDirectory>/modularr.json to ModularrOptions (validated at startup) and registers the module services.
    public static IServiceCollection AddModularrCore(this IServiceCollection services, string configDirectory)
    {
        configDirectory = Path.GetFullPath(configDirectory);
        Directory.CreateDirectory(configDirectory);

        IConfiguration file = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(configDirectory, ModularrOptions.FileName), optional: true, reloadOnChange: false)
            .Build();

        services.AddOptions<ModularrOptions>()
            .Bind(file, binder => binder.ErrorOnUnknownConfiguration = true)
            .PostConfigure(options => Resolve(options, file, configDirectory))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<TorrentEngineHost>();
        services.AddHostedService(provider => provider.GetRequiredService<TorrentEngineHost>());

        // Redirects are handled per search path (Cardigann "followredirect"), so the handler must not follow them itself.
        services.AddHttpClient<CardigannSearcher>(client => client.DefaultRequestHeaders.UserAgent.ParseAdd("Modularr/1.0"))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All });

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
