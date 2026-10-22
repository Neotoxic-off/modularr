// Cases from the Radarr SceneCheckerFixture (GPL-3.0).
using Modularr.Core.Parsing.Movies;

namespace Modularr.Core.Tests.Parsing.Movies;

public sealed class MovieSceneCheckerTests
{
    [Theory]
    [InlineData("Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-Pate")]
    public void ReturnsTrueForSceneNames(string title)
    {
        Assert.True(MovieTitleParser.IsSceneTitle(title));
    }

    [Theory]
    [InlineData("S08E05 - Virtual In-Stanity [WEBDL-720p]")]
    [InlineData("S08E05 - Virtual In-Stanity.With.Dots [WEBDL-720p]")]
    [InlineData("Something")]
    [InlineData("86de66b7ef385e2fa56a3e41b98481ea1658bfab")]
    // no group
    [InlineData("Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-")]
    // no quality
    [InlineData("Some.Movie.2..2013.German.DTS.DL-Pate")]
    // no movietitle
    [InlineData("2013.German.DTS.DL.BluRay.x264-Pate")]
    public void ReturnsFalseForNonSceneNames(string title)
    {
        Assert.False(MovieTitleParser.IsSceneTitle(title));
    }

    [Theory]
    // underscore at the end
    [InlineData("Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-Pate_", "Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-Pate")]
    // file extension
    [InlineData("Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-Pate.mkv", "Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-Pate")]
    // file extension
    [InlineData("Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-Pate.nzb", "Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-Pate")]
    // brackets
    [InlineData("Some.Movie.2..2013.German.DTS.DL.【720p】.BluRay.x264-Pate.nzb", "Some.Movie.2..2013.German.DTS.DL.[720p].BluRay.x264-Pate")]
    public void ParsesSceneNames(string title, string result)
    {
        Assert.Equal(result, MovieTitleParser.GetSceneTitle(title));
    }
}
