// Ported from Sonarr/Radarr DecisionEngineTests MaximumSize, MinimumAge, QualityAllowedByProfile, RawDisk and Search/TorrentSeeding
// specification fixtures (identical in both, run for series and movies) and Radarr LanguageSpecificationFixture.
// NotSample and Blocklist have no upstream specification fixture: their cases are new.
using Modularr.Core.Decisions;
using Modularr.Core.Languages;
using Modularr.Core.Parsing;
using Modularr.Core.Qualities;
using static Modularr.Core.Tests.Decisions.DecisionTestData;

namespace Modularr.Core.Tests.Decisions;

public sealed class ReleaseSpecificationTests
{
    public static TheoryData<MediaType> MediaTypes => [MediaType.Series, MediaType.Movie];

    private static ReleaseCandidate WithRelease(MediaType mediaType, string title = "Title.S01E01.720p.HDTV-Group", long? size = null, int? seeders = null) =>
        Candidate(mediaType) with { Release = Release(title, size) with { Seeders = seeders } };

    [Theory]
    [InlineData(0, 1000, true)]
    [InlineData(2000, 1999, true)]
    [InlineData(2000, 2000, true)]
    [InlineData(2000, 2001, false)]
    [InlineData(2000, 0, true)]
    public void MaximumSize(int maximumSize, int sizeInMegabytes, bool expected)
    {
        foreach (MediaType mediaType in new[] { MediaType.Series, MediaType.Movie })
        {
            Rejection? rejection = DownloadSpecifications.MaximumSize(WithRelease(mediaType, size: sizeInMegabytes * Megabyte), Context(new DecisionOptions { MaximumSize = maximumSize }));

            Assert.Equal(expected, rejection is null);
        }
    }

    [Theory]
    [InlineData(0, 100, true)]
    [InlineData(30, 100, true)]
    [InlineData(30, 10, false)]
    public void MinimumAge(int minimumAge, int ageInMinutes, bool expected)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ReleaseCandidate candidate = Candidate(MediaType.Series) with { Release = Release() with { PublishDate = now.AddMinutes(-ageInMinutes) } };

        Rejection? rejection = DownloadSpecifications.MinimumAge(candidate, Context(new DecisionOptions { MinimumAge = minimumAge }) with { Now = now });

        Assert.Equal(expected, rejection is null);
        Assert.True(rejection is null || rejection.Type == RejectionType.Temporary);
    }

    [Theory]
    [InlineData(MediaType.Series, 2, true)]
    [InlineData(MediaType.Series, 4, true)]
    [InlineData(MediaType.Series, 7, true)]
    [InlineData(MediaType.Series, 1, false)]
    [InlineData(MediaType.Series, 5, false)]
    [InlineData(MediaType.Series, 6, false)]
    [InlineData(MediaType.Movie, 2, true)]
    [InlineData(MediaType.Movie, 4, true)]
    [InlineData(MediaType.Movie, 7, true)]
    [InlineData(MediaType.Movie, 1, false)]
    [InlineData(MediaType.Movie, 5, false)]
    [InlineData(MediaType.Movie, 6, false)]
    public void QualityAllowedByProfile(MediaType mediaType, int qualityId, bool expected)
    {
        // Allowed: DVD (2), HDTV-720p (4), Bluray-1080p (7). Denied: SDTV (1), WEBDL-720p (5), Bluray-720p (6).
        QualityProfile profile = new() { Cutoff = Quality.Bluray1080p.Id, Items = DefaultQualities(mediaType, Quality.Dvd, Quality.Hdtv720p, Quality.Bluray1080p) };
        ReleaseCandidate candidate = Candidate(mediaType, Q(Quality.FindById(qualityId), 2), profile);

        Assert.Equal(expected, DownloadSpecifications.QualityAllowedByProfile(candidate, Context()) is null);
    }

    // Parsed titles: MULTi adds the original track (and French when no dub is named), VOSTFR is the original with French
    // subtitles, an untagged release is in the original language.
    [Theory]
    [InlineData("Movie.Title.2020.MULTi.1080p.BluRay.x264-GRP", "English", "VF", true)]
    [InlineData("Movie.Title.2020.MULTi.1080p.BluRay.x264-GRP", "English", "VO", true)]
    [InlineData("Movie.Title.2020.MULTi.1080p.BluRay.x264-GRP", "English", "MULTi", true)]
    [InlineData("Movie.Title.2020.MULTi.1080p.BluRay.x264-GRP", "English", "English", true)]
    [InlineData("Movie.Title.2020.MULTi.1080p.BluRay.x264-GRP", "English", "German", false)]
    [InlineData("Movie.Title.2020.MULTi.1080p.BluRay.x264-GRP", "English", "VFQ", false)]
    [InlineData("Movie.Title.2020.MULTi.VFF.1080p.BluRay.x264-GRP", "Japanese", "Japanese", true)]
    [InlineData("Movie.Title.2020.MULTi.VFQ.1080p.WEB.x264-GRP", "English", "VFQ", true)]
    [InlineData("Movie.Title.2020.MULTi.VFQ.1080p.WEB.x264-GRP", "English", "VF", false)]
    [InlineData("Movie.Title.2020.MULTi.VFQ.1080p.WEB.x264-GRP", "English", "MULTi", true)]
    [InlineData("Movie.Title.2020.MULTi.VFF.VFQ.1080p.WEB.x264-GRP", "English", "VF", true)]
    [InlineData("Movie.Title.2020.VFQ.1080p.WEB.x264-GRP", "English", "VFQ", true)]
    [InlineData("Movie.Title.2020.VFQ.1080p.WEB.x264-GRP", "English", "MULTi", false)]
    [InlineData("Movie.Title.2020.MULTi.TRUEFRENCH.1080p.WEB.x264-GRP", "English", "VF", true)]
    [InlineData("Movie.Title.2020.TRUEFRENCH.1080p.WEB.x264-GRP", "English", "VF", true)]
    [InlineData("Movie.Title.2020.TRUEFRENCH.1080p.WEB.x264-GRP", "English", "VO", false)]
    [InlineData("Movie.Title.2020.TRUEFRENCH.1080p.WEB.x264-GRP", "English", "MULTi", false)]
    [InlineData("Movie.Title.2020.FRENCH.1080p.WEB.x264-GRP", "French", "VO", true)]
    [InlineData("Movie.Title.2020.1080p.WEB.x264-GRP", "French", "VF", true)]
    [InlineData("Movie.Title.2020.VOSTFR.1080p.WEB.x264-GRP", "Japanese", "VF", false)]
    [InlineData("Movie.Title.2020.VOSTFR.1080p.WEB.x264-GRP", "Japanese", "VO", true)]
    [InlineData("Movie.Title.2020.VOSTFR.1080p.WEB.x264-GRP", "Japanese", "VOSTFR", true)]
    [InlineData("Movie.Title.2020.SUBFRENCH.1080p.WEB.x264-GRP", "Japanese", "VOSTFR", true)]
    [InlineData("Movie.Title.2020.1080p.WEB.x264-GRP", "Japanese", "VOSTFR", false)]
    [InlineData("Movie.Title.2020.VOSTA.1080p.WEB.x264-GRP", "Japanese", "VOSTFR", false)]
    [InlineData("Movie.Title.2020.MULTi.VOSTFR.1080p.WEB.x264-GRP", "Japanese", "VOSTFR", true)]
    [InlineData("Movie.Title.2020.1080p.WEB.x264-GRP", "English", "English", true)]
    [InlineData("Movie.Title.2020.1080p.WEB.x264-GRP", "English", "VF", false)]
    [InlineData("Movie.Title.2020.1080p.WEB.x264-GRP", "English", "Any", true)]
    [InlineData("Movie.Title.2020.Eng.Fre.Multi.Subs.1080p.WEB.x264-GRP", "Japanese", "Japanese", false)]
    public void WantedVersionOfParsedTitle(string title, string original, string wanted, bool expected)
    {
        ReleaseCandidate candidate = Candidate(MediaType.Movie) with
        {
            Languages = LanguageParser.ParseReleaseLanguages(title),
            OriginalLanguage = Language.FindByName(original),
        };

        Assert.Equal(expected, DownloadSpecifications.WantedVersion(candidate, Context() with { WantedVersion = ReleaseVersion.FindByName(wanted) }) is null);
    }

    [Theory]
    [MemberData(nameof(MediaTypes))]
    public void AcceptsReleaseNotMatchingDiscPattern(MediaType mediaType)
    {
        Assert.Null(DownloadSpecifications.RawDisk(WithRelease(mediaType, "Series.title.s01e01"), Context()));
    }

    [Theory]
    [InlineData("Series Title S02 Disc 1 1080i Blu-ray DTS-HD MA 2.0 AVC-TrollHD")]
    [InlineData("Series Title S03 Disc 1 1080p Blu-ray LPCM 2.0 AVC-TrollHD")]
    [InlineData("SERIES TITLE S02 1080P FULL BLURAY AVC DTS-HD MA 5 1")]
    [InlineData("Series.Title.S06.2016.DISC.3.BluRay.1080p.AVC.Atmos.TrueHD7.1-MTeam")]
    [InlineData("Series Title S05 Disc 1 BluRay 1080p AVC Atmos TrueHD 7 1-MTeam")]
    [InlineData("Someone.the.Entertainer.Presents.S01.NTSC.3xDVD9.MPEG-2.DD2.0")]
    [InlineData("Series.Title.S00.The.Christmas.Special.2011.PAL.DVD5.DD2.0")]
    [InlineData("Series.of.Desire.2000.S1_D01.NTSC.DVD5")]
    public void RejectsDiscFormat(string title)
    {
        foreach (MediaType mediaType in new[] { MediaType.Series, MediaType.Movie })
        {
            Assert.Equal(DownloadRejectionReason.Raw, DownloadSpecifications.RawDisk(WithRelease(mediaType, title), Context())?.Reason);
        }
    }

    [Theory]
    [InlineData("Series Title EP50 USLT NTSC DVDRemux DD2.0")]
    [InlineData("Series.Title.S01.NTSC.DVDRip.DD2.0.x264-PLAiD")]
    public void AcceptsDvdRip(string title)
    {
        Assert.Null(DownloadSpecifications.RawDisk(WithRelease(MediaType.Series, title), Context()));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(0, false)]
    [InlineData(4, false)]
    public void TorrentSeeding(int? seeders, bool expected)
    {
        foreach (MediaType mediaType in new[] { MediaType.Series, MediaType.Movie })
        {
            Rejection? rejection = DownloadSpecifications.TorrentSeeding(WithRelease(mediaType, seeders: seeders), Context(new DecisionOptions { MinimumSeeders = 5 }));

            Assert.Equal(expected, rejection is null);
        }
    }

    [Theory]
    [InlineData("Title.S01E01.720p.HDTV-Group.sample", 10, false)]
    [InlineData("Title.S01E01.720p.HDTV-Group.SAMPLE", 0, false)]
    [InlineData("Title.S01E01.720p.HDTV-Group.sample", 100, true)]
    [InlineData("Title.S01E01.720p.HDTV-Group", 10, true)]
    public void NotSample(string title, int sizeInMegabytes, bool expected)
    {
        Assert.Equal(expected, DownloadSpecifications.NotSample(WithRelease(MediaType.Series, title, sizeInMegabytes * Megabyte), Context()) is null);
    }

    [Theory]
    [InlineData("ABCDEF", "Other", null, "abcdef", false)]
    [InlineData("ABCDEF", "Other", null, "123456", true)]
    [InlineData(null, "Title.S01E01.720p.HDTV-Group", "indexer", null, false)]
    [InlineData(null, "Title.S01E01.720p.HDTV-Group", null, null, false)]
    [InlineData(null, "Title.S01E01.720p.HDTV-Group", "Other Indexer", null, true)]
    [InlineData(null, "Another.Title.S01E01", null, null, true)]
    public void Blocklist(string? releaseHash, string blockedTitle, string? blockedIndexer, string? blockedHash, bool expected)
    {
        ReleaseCandidate candidate = Candidate(MediaType.Series) with { Release = Release() with { InfoHash = releaseHash } };
        DecisionContext context = Context() with { Blocklist = [new BlocklistEntry(blockedTitle, blockedHash, blockedIndexer)] };

        Assert.Equal(expected, DownloadSpecifications.Blocklist(candidate, context) is null);
    }
}
