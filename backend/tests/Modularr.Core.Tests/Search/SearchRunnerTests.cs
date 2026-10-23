using Microsoft.Extensions.Logging.Abstractions;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Modularr.Core.Activities;
using Modularr.Core.Persistence;
using Modularr.Core.Search;

namespace Modularr.Core.Tests.Search;

public sealed class SearchRunnerTests
{
    [Fact]
    public async Task AnHttpTimeoutFailsTheJobAndTheQueueGoesOn()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using SqliteConnection connection = new("Data Source=:memory:");
        await using ServiceProvider provider = new ServiceCollection()
            .AddDbContext<ModularrDbContext>(builder => builder.UseSqlite(connection).AddInterceptors(new TimeoutInterceptor()))
            .AddSingleton<ActivityFeed>()
            .AddScoped<ActivityLog>()
            .AddScoped(services => new AutoSearch(services.GetRequiredService<ModularrDbContext>(), null!, null!, services.GetRequiredService<ActivityLog>()))
            .BuildServiceProvider();
        using SearchRunner runner = new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SearchRunner>.Instance);
        await runner.StartAsync(ct);

        runner.Enqueue(new SearchJob(SeriesId: null, MovieId: 1));
        runner.Enqueue(new SearchJob(SeriesId: null, MovieId: 2));

        while (runner.LastSearch(null, 2) is null)
        {
            Assert.False(runner.ExecuteTask!.IsCompleted, "The runner stopped.");
            await Task.Delay(10, ct);
        }

        Assert.StartsWith("Failed:", runner.LastSearch(null, 1)!.Result, StringComparison.Ordinal);
        Assert.StartsWith("Failed:", runner.LastSearch(null, 2)!.Result, StringComparison.Ordinal);
        await runner.StopAsync(ct);
    }

    // What HttpClient throws on a timeout, raised here by the first database read of the search.
    private sealed class TimeoutInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
    }
}
