using Modularr.Core.Activities;

namespace Modularr.Core.Tests.Activities;

public sealed class ActivityFeedTests
{
    [Fact]
    public async Task ASlowSubscriberKeepsTheNewestEventsAndLeavesOnCancel()
    {
        ActivityFeed feed = new();
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        IAsyncEnumerator<ServerEvent?> events = feed.SubscribeAsync(TimeSpan.FromMilliseconds(10), cancel.Token).GetAsyncEnumerator(cancel.Token);

        // The first heartbeat: subscribed, and suspended rather than reading while the events are published.
        Assert.True(await events.MoveNextAsync());
        Assert.Null(events.Current);
        Assert.True(feed.HasSubscribers);

        for (int i = 1; i <= 150; i++)
        {
            feed.Publish(new ServerEvent("progress", i));
        }

        List<int> received = [];
        while (received.Count < ActivityFeed.Capacity && await events.MoveNextAsync())
        {
            received.Add((int)events.Current!.Data);
        }

        Assert.Equal(Enumerable.Range(51, 100), received);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await events.MoveNextAsync());
        await events.DisposeAsync();
        Assert.False(feed.HasSubscribers);
    }

    [Fact]
    public void PublishingWithoutSubscribersDoesNothing()
    {
        ActivityFeed feed = new();

        feed.Publish(new ServerEvent("activity", 1));

        Assert.False(feed.HasSubscribers);
    }
}
