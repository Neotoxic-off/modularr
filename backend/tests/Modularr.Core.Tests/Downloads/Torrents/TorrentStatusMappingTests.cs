using Modularr.Core.Downloads.Torrents;
using MonoTorrent.Client;

namespace Modularr.Core.Tests.Downloads.Torrents;

// Cases ported from the Sonarr and Radarr QBittorrentFixture (GPL-3.0), qBittorrent states translated to MonoTorrent states.
public sealed class TorrentStatusMappingTests
{
    private static readonly TorrentEngineOptions NoLimits = new() { StateDirectory = "unused" };

    [Fact]
    public void ErrorItemIsWarning()
    {
        (DownloadItemStatus status, string? message) = TorrentStatusMapping.Map(TorrentState.Error, complete: false, dhtEnabled: true, "disk full");
        Assert.Equal(DownloadItemStatus.Warning, status);
        Assert.Contains("disk full", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TorrentState.Stopped)]
    [InlineData(TorrentState.Stopping)]
    [InlineData(TorrentState.Paused)]
    public void PausedItemHasRequiredProperties(TorrentState state)
    {
        DownloadItemStatus status = TorrentStatusMapping.Map(state, complete: false, dhtEnabled: true).Status;
        Assert.Equal(DownloadItemStatus.Paused, status);
        Assert.Null(TorrentStatusMapping.RemainingTime(status, 300, 0));
    }

    [Theory]
    [InlineData(TorrentState.Stopped)]
    [InlineData(TorrentState.Stopping)]
    [InlineData(TorrentState.Paused)]
    [InlineData(TorrentState.Seeding)]
    public void CompletedItemHasRequiredProperties(TorrentState state)
    {
        DownloadItemStatus status = TorrentStatusMapping.Map(state, complete: true, dhtEnabled: true).Status;
        Assert.Equal(DownloadItemStatus.Completed, status);
        Assert.Equal(TimeSpan.Zero, TorrentStatusMapping.RemainingTime(status, 0, 0));
    }

    [Theory]
    [InlineData(TorrentState.Starting)]
    [InlineData(TorrentState.Hashing)]
    [InlineData(TorrentState.HashingPaused)]
    [InlineData(TorrentState.FetchingHashes)]
    [InlineData(TorrentState.Metadata)]
    public void QueuedItemHasRequiredProperties(TorrentState state)
    {
        DownloadItemStatus status = TorrentStatusMapping.Map(state, complete: false, dhtEnabled: true).Status;
        Assert.Equal(DownloadItemStatus.Queued, status);
        Assert.Null(TorrentStatusMapping.RemainingTime(status, 300, 0));
    }

    [Fact]
    public void MetadataWithoutDhtIsWarning()
    {
        Assert.Equal(DownloadItemStatus.Warning, TorrentStatusMapping.Map(TorrentState.Metadata, complete: false, dhtEnabled: false).Status);
    }

    [Fact]
    public void DownloadingItemHasRequiredProperties()
    {
        DownloadItemStatus status = TorrentStatusMapping.Map(TorrentState.Downloading, complete: false, dhtEnabled: true).Status;
        Assert.Equal(DownloadItemStatus.Downloading, status);
        Assert.Equal(TimeSpan.FromSeconds(60), TorrentStatusMapping.RemainingTime(status, 300, 5));
    }

    [Fact]
    public void DownloadingItemWithoutRateHasNoRemainingTime()
    {
        Assert.Null(TorrentStatusMapping.RemainingTime(DownloadItemStatus.Downloading, 300, 0));
    }

    [Fact]
    public void WarningItemHasNoRemainingTime()
    {
        Assert.Null(TorrentStatusMapping.RemainingTime(DownloadItemStatus.Warning, 300, 5));
    }

    [Fact]
    public void ShouldNotBeRemovableIfMaxRatioNotReached()
    {
        Assert.False(TorrentStatusMapping.CanBeRemoved(TorrentState.Seeding, true, 0.5, null, NoLimits with { SeedRatioLimit = 1.0 }));
    }

    [Fact]
    public void ShouldNotBeRemovableIfMaxRatioReachedAndNotPaused()
    {
        Assert.False(TorrentStatusMapping.CanBeRemoved(TorrentState.Seeding, true, 1.0, null, NoLimits with { SeedRatioLimit = 1.0 }));
    }

    [Fact]
    public void ShouldNotBeRemovableIfMaxRatioIsNotSet()
    {
        Assert.False(TorrentStatusMapping.CanBeRemoved(TorrentState.Stopped, true, 1.0, null, NoLimits));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.1006066990976857)]
    [InlineData(0.9999)]
    public void ShouldBeRemovableIfMaxRatioReachedAndPaused(double ratio)
    {
        Assert.True(TorrentStatusMapping.CanBeRemoved(TorrentState.Stopped, true, ratio, null, NoLimits with { SeedRatioLimit = 1.0 }));
    }

    [Fact]
    public void ShouldNotBeRemovableIfMaxSeedingTimeReachedAndNotPaused()
    {
        TorrentEngineOptions options = NoLimits with { SeedTimeLimit = TimeSpan.FromMinutes(20) };
        Assert.False(TorrentStatusMapping.CanBeRemoved(TorrentState.Seeding, true, 2.0, TimeSpan.FromMinutes(30), options));
    }

    [Fact]
    public void ShouldBeRemovableIfMaxSeedingTimeReachedAndPaused()
    {
        TorrentEngineOptions options = NoLimits with { SeedTimeLimit = TimeSpan.FromMinutes(20) };
        Assert.True(TorrentStatusMapping.CanBeRemoved(TorrentState.Stopped, true, 2.0, TimeSpan.FromMinutes(20), options));
    }

    [Fact]
    public void ShouldBeRemovableIfMaxSeedingTimeReachedButRatioNotAndPaused()
    {
        TorrentEngineOptions options = NoLimits with { SeedRatioLimit = 2.0, SeedTimeLimit = TimeSpan.FromMinutes(20) };
        Assert.True(TorrentStatusMapping.CanBeRemoved(TorrentState.Stopped, true, 1.0, TimeSpan.FromMinutes(30), options));
    }

    [Fact]
    public void ShouldNotBeRemovableIfNotComplete()
    {
        Assert.False(TorrentStatusMapping.CanBeRemoved(TorrentState.Stopped, false, 5.0, null, NoLimits with { SeedRatioLimit = 1.0 }));
    }
}
