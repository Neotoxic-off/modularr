using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Modularr.Core.Library;

// Runs LibraryService.BackfillMetadataAsync once per start, off the startup path: it calls the metadata sources per item.
public sealed class MetadataBackfill(IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<LibraryService>().BackfillMetadataAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception) when (!stoppingToken.IsCancellationRequested)
        {
            // An exception escaping ExecuteAsync stops the host; a busy database just leaves the backfill for the next start.
        }
    }
}
