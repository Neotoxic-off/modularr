using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;

namespace Modularr.Core.Library;

// Refreshes every title's metadata (new episodes, air and release days) 10 minutes after start, then every RefreshIntervalHours.
public sealed class MetadataRefresher(IServiceScopeFactory scopes, IOptionsMonitor<ModularrOptions> options, ILogger<MetadataRefresher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            int hours = options.CurrentValue.Automation.RefreshIntervalHours;
            if (hours > 0)
            {
                try
                {
                    await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<LibraryService>().RefreshAllAsync(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                    await scope.ServiceProvider.GetRequiredService<CollectionService>().RefreshFollowedAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.JobFailed("Metadata refresh", ex);
                    // An exception escaping ExecuteAsync stops the host; a busy database is refreshed on the next round.
                }
            }

            // Turned off: the setting is read again in an hour.
            await Task.Delay(TimeSpan.FromHours(hours > 0 ? hours : 1), stoppingToken).ConfigureAwait(false);
        }
    }
}
