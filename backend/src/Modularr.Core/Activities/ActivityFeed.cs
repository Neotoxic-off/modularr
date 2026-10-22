using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Modularr.Core.Activities;

// What a live client receives: "activity" with an Activity, "progress" with the running downloads' DownloadProgress.
public sealed record ServerEvent(string Type, object Data);

// Progress 0 to 1, rate in bytes per second, eta in seconds.
public sealed record DownloadProgress(int Id, double Progress, long Rate, long? Eta);

// In-process publish/subscribe for the live clients. Publishing never waits: a subscriber that falls behind loses its oldest events.
public sealed class ActivityFeed
{
    public const int Capacity = 100;

    private readonly ConcurrentDictionary<Channel<ServerEvent>, byte> _subscribers = [];

    public bool HasSubscribers => !_subscribers.IsEmpty;

    public void Publish(ServerEvent serverEvent)
    {
        foreach (Channel<ServerEvent> subscriber in _subscribers.Keys)
        {
            subscriber.Writer.TryWrite(serverEvent);
        }
    }

    // Subscribed from the first MoveNextAsync until the enumeration ends; yields null when nothing came for a heartbeat interval.
    public async IAsyncEnumerable<ServerEvent?> SubscribeAsync(TimeSpan heartbeat, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Channel<ServerEvent> channel = Channel.CreateBounded<ServerEvent>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        _subscribers.TryAdd(channel, 0);
        try
        {
            while (true)
            {
                if (!await WaitAsync(channel.Reader, heartbeat, cancellationToken).ConfigureAwait(false))
                {
                    yield return null;
                    continue;
                }

                while (channel.Reader.TryRead(out ServerEvent? serverEvent))
                {
                    yield return serverEvent;
                }
            }
        }
        finally
        {
            _subscribers.TryRemove(channel, out _);
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
}
