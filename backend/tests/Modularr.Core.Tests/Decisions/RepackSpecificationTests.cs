// Ported from Sonarr and Radarr DecisionEngineTests/RepackSpecificationFixture (identical, run for series and movies).
using Modularr.Core.Decisions;
using Modularr.Core.Qualities;
using static Modularr.Core.Tests.Decisions.DecisionTestData;

namespace Modularr.Core.Tests.Decisions;

public sealed class RepackSpecificationTests
{
    public static TheoryData<MediaType> MediaTypes => [MediaType.Series, MediaType.Movie];

    private static ReleaseCandidate Repack(MediaType mediaType, ExistingFile? file, bool isRepack = true, string? releaseGroup = "Modularr") =>
        Candidate(mediaType, new QualityModel(Quality.Sdtv, new Revision(2, 0, isRepack))) with
        {
            ReleaseGroup = releaseGroup,
            MovieFile = mediaType == MediaType.Movie ? file : null,
            Episodes = mediaType == MediaType.Movie ? [] : [Episode(file: file)],
        };

    private static bool Accepted(ReleaseCandidate candidate, ProperDownloadType propers = ProperDownloadType.PreferAndUpgrade) =>
        DownloadSpecifications.Repack(candidate, Context(new DecisionOptions { DownloadPropersAndRepacks = propers })) is null;

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void AcceptsNotARepack(MediaType mediaType)
    {
        Assert.True(Accepted(Repack(mediaType, new ExistingFile(Q(Quality.Sdtv), "Other"), isRepack: false)));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void AcceptsWithoutExistingFile(MediaType mediaType)
    {
        Assert.True(Accepted(Repack(mediaType, null)));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void AcceptsRepackForADifferentQuality(MediaType mediaType)
    {
        Assert.True(Accepted(Repack(mediaType, new ExistingFile(Q(Quality.Dvd), "Modularr"))));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void AcceptsRepackForExistingFile(MediaType mediaType)
    {
        Assert.True(Accepted(Repack(mediaType, new ExistingFile(Q(Quality.Sdtv), "Modularr"))));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void RejectsRepackForADifferentGroup(MediaType mediaType)
    {
        Rejection? rejection = DownloadSpecifications.Repack(Repack(mediaType, new ExistingFile(Q(Quality.Sdtv), "NotModularr")), Context());

        Assert.Equal(DownloadRejectionReason.RepackReleaseGroupDoesNotMatch, rejection?.Reason);
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void RejectsWhenExistingFileReleaseGroupIsUnknown(MediaType mediaType)
    {
        Assert.False(Accepted(Repack(mediaType, new ExistingFile(Q(Quality.Sdtv), ""))));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void RejectsWhenReleaseGroupIsUnknown(MediaType mediaType)
    {
        Assert.False(Accepted(Repack(mediaType, new ExistingFile(Q(Quality.Sdtv), "Modularr"), releaseGroup: null)));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void AcceptsWhenRepacksAreNotPreferred(MediaType mediaType)
    {
        Assert.True(Accepted(Repack(mediaType, new ExistingFile(Q(Quality.Sdtv), "Modularr")), ProperDownloadType.DoNotPrefer));
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void RejectsWhenRepacksAreNotUpgraded(MediaType mediaType)
    {
        Rejection? rejection = DownloadSpecifications.Repack(
            Repack(mediaType, new ExistingFile(Q(Quality.Sdtv), "Modularr")),
            Context(new DecisionOptions { DownloadPropersAndRepacks = ProperDownloadType.DoNotUpgrade }));

        Assert.Equal(DownloadRejectionReason.RepackDisabled, rejection?.Reason);
    }
}
