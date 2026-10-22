// Ported from Sonarr/Radarr DecisionEngineTests/DownloadDecisionMakerFixture (spec aggregation cases) with real specifications
// instead of mocks; the parsing/mapping cases belong to the caller and are skipped.
using Modularr.Core.Decisions;
using Modularr.Core.Qualities;
using static Modularr.Core.Tests.Decisions.DecisionTestData;

namespace Modularr.Core.Tests.Decisions;

public sealed class DownloadDecisionMakerTests
{
    private static ReleaseCandidate Acceptable(MediaType mediaType) =>
        Candidate(mediaType, Q(Quality.Hdtv720p)) with
        {
            Release = Release("Title.S01E01.720p.HDTV-Group", 900 * Megabyte) with { Seeders = 50 },
            Runtime = 45,
            Episodes = mediaType == MediaType.Movie ? [] : [Episode(runtime: 45)],
        };

    [Theory]
    [InlineData(MediaType.Series)]
    [InlineData(MediaType.Anime)]
    [InlineData(MediaType.Movie)]
    public void ApprovesWhenAllSpecificationsPass(MediaType mediaType)
    {
        DownloadDecision decision = DownloadDecisionMaker.GetDecision(Acceptable(mediaType), Context());

        Assert.True(decision.Approved);
        Assert.False(decision.Rejected);
        Assert.False(decision.TemporarilyRejected);
    }

    [Fact]
    public void RejectsWhenOneSpecificationFails()
    {
        ReleaseCandidate candidate = Acceptable(MediaType.Series) with { Release = Acceptable(MediaType.Series).Release with { Seeders = 0 } };

        DownloadDecision decision = DownloadDecisionMaker.GetDecision(candidate, Context());

        Assert.False(decision.Approved);
        Assert.Equal(DownloadRejectionReason.MinimumSeeders, Assert.Single(decision.Rejections).Reason);
    }

    [Fact]
    public void ListsEveryFailedSpecification()
    {
        ReleaseCandidate candidate = Acceptable(MediaType.Series) with
        {
            Release = Release("Title.S01E01.720p.HDTV-Group.sample", 10 * Megabyte) with { Seeders = 0 },
            MultiSeason = true,
        };

        DownloadDecision decision = DownloadDecisionMaker.GetDecision(candidate, Context());

        Assert.Equal(
            new[] { DownloadRejectionReason.BelowMinimumSize, DownloadRejectionReason.MinimumSeeders, DownloadRejectionReason.Sample, DownloadRejectionReason.MultiSeason },
            decision.Rejections.Select(r => r.Reason));
        Assert.True(decision.Rejected);
    }

    [Fact]
    public void OnlyTemporaryRejectionsAreTemporarilyRejected()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ReleaseCandidate candidate = Acceptable(MediaType.Movie) with { Release = Acceptable(MediaType.Movie).Release with { PublishDate = now.AddMinutes(-5) } };

        DownloadDecision decision = DownloadDecisionMaker.GetDecision(candidate, Context(new DecisionOptions { MinimumAge = 30 }) with { Now = now });

        Assert.True(decision.TemporarilyRejected);
        Assert.False(decision.Rejected);
    }
}
