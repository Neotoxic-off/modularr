using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Modularr.Core.Library;

// Scans every title's folder at startup and every 12 hours, so files added or deleted by hand show up.
public sealed class LibraryScanner(IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // SQLite queries complete synchronously: without this the first scan would run inside host startup.
        await Task.Yield();
        using PeriodicTimer timer = new(TimeSpan.FromHours(12));
        do
        {
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DiskScanService>().ScanAllAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                // An exception escaping ExecuteAsync stops the host; a busy database is scanned on the next tick.
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
