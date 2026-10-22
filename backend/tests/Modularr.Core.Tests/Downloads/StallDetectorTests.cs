using Modularr.Core.Downloads;
using Modularr.Core.Downloads.Torrents;

namespace Modularr.Core.Tests.Downloads;

public sealed class StallDetectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly TorrentEngineOptions Options = new()
    {
        MetadataTimeout = TimeSpan.FromMinutes(10),
        NoPeersTimeout = TimeSpan.FromMinutes(30),
        NoProgressTimeout = TimeSpan.FromHours(1),
        MinimumAverageSpeed = 1000,
        SlowSpeedWindow = TimeSpan.FromHours(2),
        StallRetries = 2,
    };

    [Fact]
    public void HealthyDownloadNeverStalls()
    {
        (StallCheck last, _) = Run(TimeSpan.FromHours(10), minute => Status(seeds: 3, bytes: minute * 1_000_000));

        Assert.Equal(StallAction.None, last.Action);
    }

    [Fact]
    public void MissingMetadataIsRetriedThenGivenUp()
    {
        // Each retry restarts the 10 minute timer: retries at 10 and 20, give-up at 30, not before.
        (StallCheck last, List<StallCheck> stalls) = Run(TimeSpan.FromMinutes(29), _ => Status(hasMetadata: false));
        Assert.Equal(2, stalls.Count);
        (last, stalls) = Run(TimeSpan.FromMinutes(30), _ => Status(hasMetadata: false));

        Assert.Equal([StallAction.Retry, StallAction.Retry, StallAction.GiveUp], stalls.Select(s => s.Action));
        Assert.All(stalls, s => Assert.Equal("Metadata not received within 00:10:00", s.Reason));
        Assert.Equal([1, 2, 2], stalls.Select(s => s.State.Retries));
        Assert.Equal(StallAction.GiveUp, last.Action);
    }

    [Fact]
    public void MetadataInTimeIsNotAStall()
    {
        (StallCheck last, List<StallCheck> stalls) = Run(TimeSpan.FromMinutes(20), minute => Status(hasMetadata: minute >= 9, seeds: 1, bytes: minute * 1_000_000));

        Assert.Empty(stalls);
        Assert.Equal(StallAction.None, last.Action);
    }

    [Fact]
    public void NoSeedsAndNoPeersStalls()
    {
        (_, List<StallCheck> stalls) = Run(TimeSpan.FromMinutes(30), minute => Status(bytes: minute));

        StallCheck stall = Assert.Single(stalls);
        Assert.Equal((StallAction.Retry, "No seeds or peers for 00:30:00"), (stall.Action, stall.Reason));
        Assert.Equal(1, stall.State.Retries);
    }

    [Fact]
    public void ALeecherAloneKeepsTheDownloadAlive()
    {
        (_, List<StallCheck> stalls) = Run(TimeSpan.FromMinutes(45), minute => Status(peers: 1, bytes: minute * 1_000_000));

        Assert.Empty(stalls);
    }

    [Fact]
    public void PeersWithoutProgressStall()
    {
        (_, List<StallCheck> stalls) = Run(TimeSpan.FromHours(1), _ => Status(seeds: 2, bytes: 5_000));

        StallCheck stall = Assert.Single(stalls);
        Assert.Equal("No progress for 01:00:00", stall.Reason);
    }

    [Fact]
    public void SlowAverageSpeedStallsAtTheEndOfTheWindow()
    {
        // 30 kB a minute is 500 B/s, under the 1000 B/s minimum.
        (_, List<StallCheck> stalls) = Run(TimeSpan.FromHours(2), minute => Status(seeds: 1, bytes: minute * 30_000));

        StallCheck stall = Assert.Single(stalls);
        Assert.Equal("Average speed 500 B/s below 1000 B/s over 02:00:00", stall.Reason);
    }

    [Fact]
    public void FastEnoughDownloadOpensANewWindow()
    {
        // 120 kB a minute is 2000 B/s, then it drops to 30 kB a minute (500 B/s) during the second window.
        (_, List<StallCheck> stalls) = Run(TimeSpan.FromHours(4), minute => Status(seeds: 1, bytes: minute <= 120 ? minute * 120_000 : (120 * 120_000) + ((minute - 120) * 30_000)));

        StallCheck stall = Assert.Single(stalls);
        Assert.Equal(TimeSpan.FromHours(4), stall.State.Since - Start);
        Assert.StartsWith("Average speed 500 B/s", stall.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ZeroMinimumSpeedDisablesTheSpeedCheck()
    {
        (_, List<StallCheck> stalls) = Run(TimeSpan.FromHours(5), minute => Status(seeds: 1, bytes: minute), Options with { MinimumAverageSpeed = 0 });

        Assert.Empty(stalls);
    }

    [Fact]
    public void ZeroRetriesGivesUpAtOnce()
    {
        (_, List<StallCheck> stalls) = Run(TimeSpan.FromMinutes(10), _ => Status(hasMetadata: false), Options with { StallRetries = 0 });

        Assert.Equal(StallAction.GiveUp, Assert.Single(stalls).Action);
    }

    // Polls every minute like the monitor would (it polls more often), collecting the polls that found a stall; stops at give-up.
    private static (StallCheck Last, List<StallCheck> Stalls) Run(TimeSpan duration, Func<int, TorrentStatus> statusAt, TorrentEngineOptions? options = null)
    {
        StallState? state = null;
        StallCheck? check = null;
        List<StallCheck> stalls = [];
        for (int minute = 0; minute <= duration.TotalMinutes; minute++)
        {
            check = StallDetector.Evaluate(state, statusAt(minute), Start.AddMinutes(minute), options ?? Options);
            state = check.State;
            if (check.Action != StallAction.None)
            {
                stalls.Add(check);
            }

            if (check.Action == StallAction.GiveUp)
            {
                break;
            }
        }

        return (check!, stalls);
    }

    private static TorrentStatus Status(bool hasMetadata = true, int seeds = 0, int peers = 0, long bytes = 0) => new()
    {
        Id = "AA",
        Name = "Movie",
        Category = "movies",
        Status = DownloadItemStatus.Downloading,
        Progress = 0,
        TotalSize = 0,
        RemainingSize = 0,
        DownloadRate = 0,
        UploadRate = 0,
        SeedRatio = 0,
        SavePath = "",
        OutputPath = "",
        Files = [],
        CanBeRemoved = false,
        HasMetadata = hasMetadata,
        Seeds = seeds,
        Peers = peers,
        DownloadedBytes = bytes,
    };
}
