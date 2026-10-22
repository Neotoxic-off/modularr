// Cases from the Radarr HashedReleaseFixture (GPL-3.0).
using Modularr.Core.Parsing.Movies;
using Modularr.Core.Qualities;

namespace Modularr.Core.Tests.Parsing.Movies;

public sealed class MoviePathParserTests
{
    public static TheoryData<string, string, string, Quality, string?> HashedReleases => new()
    {
        { "Some.Hashed.Release.2018.720p.WEB-DL.AAC2.0.H.264-Mercury", "0e895c37245186812cb08aab1529cf8ee389dd05.mkv", "Some Hashed Release", Quality.WebDl720p, "Mercury" },
        { "0e895c37245186812cb08aab1529cf8ee389dd05", "Some.Hashed.Release.2018.720p.WEB-DL.AAC2.0.H.264-Mercury.mkv", "Some Hashed Release", Quality.WebDl720p, "Mercury" },
        { "Movie.2018.DVDRip.XviD-RADARR", "AHFMZXGHEWD660.mkv", "Movie", Quality.Dvd, "RADARR" },
        { "Movie.2018.1080p.BluRay.x264-RADARR", "Backup_72023S02-12.mkv", "Movie", Quality.Bluray1080p, "RADARR" },
        { "Movie.2018.1080p.BluRay.x264", "Backup_72023S02-12.mkv", "Movie", Quality.Bluray1080p, null },
        { "Movie 2018 720p WEB-DL DD5 1 H 264-ECI", "123.mkv", "Movie", Quality.WebDl720p, "ECI" },
        { "Movie 2018 720p WEB-DL DD5 1 H 264-ECI", "abc.mkv", "Movie", Quality.WebDl720p, "ECI" },
        { "Movie 2018 720p WEB-DL DD5 1 H 264-ECI", "b00bs.mkv", "Movie", Quality.WebDl720p, "ECI" },
        { "Movie.Title.2018.720p.HDTV.x264-NZBgeek", "cgajsofuejsa501.mkv", "Movie Title", Quality.Hdtv720p, "NZBgeek" },
        { "Movie.2018.1080p.WEB-DL.DD5.1.H264-RARBG", "170424_26.mkv", "Movie", Quality.WebDl1080p, "RARBG" },
        { "Movie.Title.2018.720p.HDTV.H.264", "abc.xyz.af6021c37f7852.mkv", "Movie Title", Quality.Hdtv720p, null },
    };

    [Theory]
    [MemberData(nameof(HashedReleases))]
    public void ParsesHashedReleases(string folder, string file, string title, Quality quality, string? releaseGroup)
    {
        ParsedMovieInfo? result = MovieTitleParser.ParseMoviePath(Path.Combine("Test", folder, file));
        Assert.NotNull(result);
        Assert.Equal(title, result.PrimaryMovieTitle);
        Assert.Equal(quality, result.Quality.Quality);
        Assert.Equal(releaseGroup, result.ReleaseGroup);
    }
}
