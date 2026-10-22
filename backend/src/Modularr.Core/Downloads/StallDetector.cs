using System.Globalization;
using Modularr.Core.Downloads.Torrents;

namespace Modularr.Core.Downloads;

public enum StallAction
{
    None,
    Retry,
    GiveUp,
}

// What the monitor saw of one download so far; the average speed is measured over windows opened at WindowStart.
public sealed record StallState(DateTimeOffset Since, DateTimeOffset LastPeerAt, DateTimeOffset LastProgressAt, long LastBytes, DateTimeOffset WindowStart, long WindowBytes, int Retries)
{
    public static StallState Start(DateTimeOffset now, long bytes, int retries) => new(now, now, now, bytes, now, bytes, retries);
}

public sealed record StallCheck(StallState State, StallAction Action, string? Reason);

// Pure stall state machine: each poll folds the torrent's status into the state; a stall is retried (timers restart)
// until the retries are spent, then given up.
public static class StallDetector
{
    public static StallCheck Evaluate(StallState? previous, TorrentStatus torrent, DateTimeOffset now, TorrentEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        ArgumentNullException.ThrowIfNull(options);
        StallState state = previous ?? StallState.Start(now, torrent.DownloadedBytes, retries: 0);
        state = state with
        {
            LastPeerAt = torrent.Seeds + torrent.Peers > 0 ? now : state.LastPeerAt,
            // != rather than >: the byte counter restarts with the app.
            LastProgressAt = torrent.DownloadedBytes != state.LastBytes ? now : state.LastProgressAt,
            LastBytes = torrent.DownloadedBytes,
        };

        bool windowEnded = now - state.WindowStart >= options.SlowSpeedWindow;
        double averageSpeed = (torrent.DownloadedBytes - state.WindowBytes) / Math.Max(1, (now - state.WindowStart).TotalSeconds);
        string? reason = torrent switch
        {
            { HasMetadata: false } when now - state.Since >= options.MetadataTimeout => $"Metadata not received within {options.MetadataTimeout}",
            { HasMetadata: false } => null,
            _ when now - state.LastPeerAt >= options.NoPeersTimeout => $"No seeds or peers for {options.NoPeersTimeout}",
            _ when now - state.LastProgressAt >= options.NoProgressTimeout => $"No progress for {options.NoProgressTimeout}",
            _ when windowEnded && averageSpeed < options.MinimumAverageSpeed => string.Create(
                CultureInfo.InvariantCulture, $"Average speed {averageSpeed:0} B/s below {options.MinimumAverageSpeed} B/s over {options.SlowSpeedWindow}"),
            _ => null,
        };

        return reason switch
        {
            null when windowEnded => new StallCheck(state with { WindowStart = now, WindowBytes = torrent.DownloadedBytes }, StallAction.None, null),
            null => new StallCheck(state, StallAction.None, null),
            _ when state.Retries < options.StallRetries => new StallCheck(StallState.Start(now, torrent.DownloadedBytes, state.Retries + 1), StallAction.Retry, reason),
            _ => new StallCheck(state, StallAction.GiveUp, reason),
        };
    }
}
