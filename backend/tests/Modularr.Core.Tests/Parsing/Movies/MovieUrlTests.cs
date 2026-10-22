// Cases from the Radarr UrlFixture (GPL-3.0).
using Modularr.Core.Parsing;
using Modularr.Core.Parsing.Movies;

namespace Modularr.Core.Tests.Parsing.Movies;

public sealed class MovieUrlTests
{
    [Theory]
    [InlineData("[www.test.com] - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
    [InlineData("test.net - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
    [InlineData("[www.test-hyphen.com] - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
    [InlineData("www.test123.org - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
    [InlineData("[test.co.uk] - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
    [InlineData("www.test-hyphen.net.au - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
    [InlineData("[www.test123.co.nz] - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
    [InlineData("test-hyphen123.org.au - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
    [InlineData("[www.test123.de] - Mad Movie Title 2023 [Bluray720p]", "Mad Movie Title")]
    [InlineData("www.test-hyphen.de - Mad Movie Title 2023 [Bluray1080p]", "Mad Movie Title")]
    [InlineData("www.test123.co.za - The Movie Title Bros. (2023)", "The Movie Title Bros.")]
    [InlineData("[www.test-hyphen.ca] - Movie Title (2023)", "Movie Title")]
    [InlineData("test123.ca - Movie Time 2023 720p HDTV x264 CRON", "Movie Time")]
    [InlineData("[www.test-hyphen123.co.za] - Movie Title 2023", "Movie Title")]
    [InlineData("(movieawake.com) Movie Title 2023 [720p] [English Subbed]", "Movie Title")]
    public void DoesNotParseUrlInName(string postTitle, string title)
    {
        ParsedMovieInfo? parsed = MovieTitleParser.ParseMovieTitle(postTitle);
        Assert.NotNull(parsed);
        Assert.Equal(MovieTitleParser.CleanMovieTitle(title), MovieTitleParser.CleanMovieTitle(parsed.PrimaryMovieTitle));
    }

    [Theory]
    [InlineData("Movie.2023.English.HDTV.XviD-LOL[www.abb.com]", "LOL")]
    [InlineData("Movie Title 2023 English HDTV XviD LOL[www.academy.org]", null)]
    [InlineData("Movie Title Now 2023 DVDRip XviD RUNNER[www.aetna.net]", null)]
    [InlineData("Movie.Title.2023.DVDRip.XviD-RUNNER[www.alfaromeo.io]", "RUNNER")]
    [InlineData("Movie.Title.2023.English.HDTV.XviD-LOL[www.abbott.gov]", "LOL")]
    [InlineData("Movie Title 2023 English HDTV XviD LOL[www.actor.org]", null)]
    [InlineData("Movie Title Future 2023 DVDRip XviD RUNNER[www.allstate.net]", null)]
    public void DoesNotParseUrlInGroup(string title, string? expected)
    {
        Assert.Equal(expected, ReleaseGroupParser.ParseReleaseGroup(title));
    }
}
