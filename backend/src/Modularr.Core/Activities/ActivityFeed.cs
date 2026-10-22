using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Modularr.Core.Activities;

// What a live client receives: "activity" with an Activity, "progress" with the running downloads' DownloadProgress.
public sealed record ServerEvent(string Type, object Data)
{
    public const string Activity = "activity";
    public const string Progress = "progress";
}

// Progress 0 to 1, rate in bytes per second, eta in seconds.
public sealed record DownloadProgress(int Id, double Progress, long Rate, long? Eta);

// In-process publish/subscribe for the live clients. Publishing never waits: a subscriber that falls behind loses its oldest events,
// and keeps only the latest progress frame so progress never pushes activities out of its queue.
public sealed class ActivityFeed
{
    public const int Capacity = 100;

    private readonly ConcurrentDictionary<Subscriber, byte> _subscribers = [];

    public bool HasSubscribers => !_subscribers.IsEmpty;

    public void Publish(ServerEvent serverEvent)
    {
        ArgumentNullException.ThrowIfNull(serverEvent);
        foreach (Subscriber subscriber in _subscribers.Keys)
        {
            // A queued progress event only wakes the reader, which then takes the latest frame from the slot.
            if (serverEvent.Type != ServerEvent.Progress || Interlocked.Exchange(ref subscriber.Progress, serverEvent) is null)
            {
                subscriber.Events.Writer.TryWrite(serverEvent);
            }
        }
    }

    // Subscribed from the first MoveNextAsync until the enumeration ends; yields null when nothing came for a heartbeat interval.
    public async IAsyncEnumerable<ServerEvent?> SubscribeAsync(TimeSpan heartbeat, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Subscriber subscriber = new();
        _subscribers.TryAdd(subscriber, 0);
        try
        {
            while (true)
            {
                if (!await WaitAsync(subscriber.Events.Reader, heartbeat, cancellationToken).ConfigureAwait(false))
                {
                    yield return null;
                    continue;
                }

                while (subscriber.Events.Reader.TryRead(out ServerEvent? serverEvent))
                {
                    if (serverEvent.Type != ServerEvent.Progress)
                    {
                        yield return serverEvent;
                    }
                    else if (Interlocked.Exchange(ref subscriber.Progress, null) is ServerEvent latest)
                    {
                        yield return latest;
                    }
                }

                // The progress wake-up itself may have been dropped from a full queue.
                if (Interlocked.Exchange(ref subscriber.Progress, null) is ServerEvent pending)
                {
                    yield return pending;
                }
            }
        }
        finally
        {
            _subscribers.TryRemove(subscriber, out _);
        }
    }

    private static async Task<bool> WaitAsync(ChannelReader<ServerEvent> reader, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(timeout);
        try
        {
            return await reader.WaitToReadAsync(wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private sealed class Subscriber
    {
        public readonly Channel<ServerEvent> Events = Channel.CreateBounded<ServerEvent>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

        public ServerEvent? Progress;
    }
}
