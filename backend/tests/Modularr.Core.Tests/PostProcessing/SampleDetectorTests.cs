// Ported from Sonarr EpisodeImport DetectSampleFixture and Radarr MovieImport DetectSampleFixture (extension cases); name + size cases are new.
using Modularr.Core.PostProcessing;

namespace Modularr.Core.Tests.PostProcessing;

public sealed class SampleDetectorTests
{
    private const long Small = 10L * 1024 * 1024;

    [Theory]
    [InlineData(@"C:\Test\some.show.s01e01.sample.flv")]
    [InlineData(@"C:\Test\some.show.s01e01.sample.strm")]
    [InlineData(@"C:\Test\some movie (2000) sample.iso")]
    [InlineData(@"C:\Test\some movie (2000) sample.img")]
    [InlineData(@"C:\Test\some movie (2000) sample.m2ts")]
    public void NeverTreatsStreamsAndDiscImagesAsSamples(string path)
    {
        Assert.False(SampleDetector.IsSample(path, Small));
    }

    [Theory]
    [InlineData("/dl/Show.S01E01.720p-GRP/Show.S01E01.720p-GRP.sample.mkv")]
    [InlineData("/dl/Show.S01E01.720p-GRP/sample-show.s01e01.mkv")]
    [InlineData("/dl/Show.S01E01.720p-GRP/Show_S01E01_SAMPLE.mkv")]
    [InlineData("/dl/Show.S01E01.720p-GRP/Sample/show.s01e01.mkv")]
    [InlineData("/dl/Show.S01E01.720p-GRP/Samples/show.s01e01.mkv")]
    public void DetectsSmallFileNamedSample(string path)
    {
        Assert.True(SampleDetector.IsSample(path, Small));
    }

    [Theory]
    [InlineData("/dl/Show.S01E01.720p-GRP/Show.S01E01.720p-GRP.mkv")]
    [InlineData("/dl/Sampler.S01E01.720p-GRP/Sampler.S01E01.720p-GRP.mkv")]
    [InlineData("/dl/Show.S01E01.720p-GRP/Show.S01E01.Resampled.mkv")]
    public void DoesNotDetectSmallFileNotNamedSample(string path)
    {
        Assert.False(SampleDetector.IsSample(path, Small));
    }

    [Fact]
    public void DoesNotDetectLargeFileNamedSample()
    {
        Assert.False(SampleDetector.IsSample("/dl/Sample.Show.S01E01.720p-GRP/Sample.Show.S01E01.720p-GRP.mkv", SampleDetector.MaxSampleSize));
    }
}
