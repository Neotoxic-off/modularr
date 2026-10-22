// Cases from the Radarr EditionParserFixture (GPL-3.0).
using Modularr.Core.Parsing.Movies;

namespace Modularr.Core.Tests.Parsing.Movies;

public sealed class MovieEditionParserTests
{
    [Theory]
    [InlineData("Movie Title 2012 Directors Cut", "Directors Cut")]
    [InlineData("Movie Title 1999 (Despecialized).mkv", "Despecialized")]
    [InlineData("Movie Title.2012.(Special.Edition.Remastered).[Bluray-1080p].mkv", "Special Edition Remastered")]
    [InlineData("Movie Title 2012 Extended", "Extended")]
    [InlineData("Movie Title 2012 Extended Directors Cut Fan Edit", "Extended Directors Cut Fan Edit")]
    [InlineData("Movie Title 2012 Director's Cut", "Director's Cut")]
    [InlineData("Movie Title.2012.(Extended.Theatrical.Version.IMAX).BluRay.1080p.2012.asdf", "Extended Theatrical Version IMAX")]
    [InlineData("2021 A Movie (1968) Director's Cut .mkv", "Director's Cut")]
    [InlineData("2021 A Movie 1968 (Extended Directors Cut FanEdit)", "Extended Directors Cut FanEdit")]
    [InlineData("A Fake Movie 2035 2012 Directors.mkv", "Directors")]
    [InlineData("Movie 2049 Director's Cut.mkv", "Director's Cut")]
    [InlineData("Movie Title 2012 50th Anniversary Edition.mkv", "50th Anniversary Edition")]
    [InlineData("Movie 2012 2in1.mkv", "2in1")]
    [InlineData("Movie 2012 IMAX.mkv", "IMAX")]
    [InlineData("Movie 2012 Restored.mkv", "Restored")]
    [InlineData("Movie Title.Special.Edition.Fan Edit.2012..BRRip.x264.AAC-m2g", "Special Edition Fan Edit")]
    [InlineData("Movie Title (Despecialized) 1999.mkv", "Despecialized")]
    [InlineData("Movie Title.(Special.Edition.Remastered).2012.[Bluray-1080p].mkv", "Special Edition Remastered")]
    [InlineData("Movie Title Extended 2012", "Extended")]
    [InlineData("Movie Title Extended Directors Cut Fan Edit 2012", "Extended Directors Cut Fan Edit")]
    [InlineData("Movie Title Director's Cut 2012", "Director's Cut")]
    [InlineData("Movie Title Directors Cut 2012", "Directors Cut")]
    [InlineData("Movie Title.(Extended.Theatrical.Version.IMAX).2012.BluRay.1080p.asdf", "Extended Theatrical Version IMAX")]
    [InlineData("Movie Director's Cut (1968).mkv", "Director's Cut")]
    [InlineData("2021 A Movie (Extended Directors Cut FanEdit) 1968 Bluray 1080p", "Extended Directors Cut FanEdit")]
    [InlineData("A Fake Movie 2035 Directors 2012.mkv", "Directors")]
    [InlineData("Movie Director's Cut 2049.mkv", "Director's Cut")]
    [InlineData("Movie Title 50th Anniversary Edition 2012.mkv", "50th Anniversary Edition")]
    [InlineData("Movie 2in1 2012.mkv", "2in1")]
    [InlineData("Movie IMAX 2012.mkv", "IMAX")]
    [InlineData("Fake Movie Final Cut 2016", "Final Cut")]
    [InlineData("Fake Movie 2016 Final Cut ", "Final Cut")]
    [InlineData("My Movie GERMAN Extended Cut 2016", "Extended Cut")]
    [InlineData("My.Movie.GERMAN.Extended.Cut.2016", "Extended Cut")]
    [InlineData("My.Movie.GERMAN.Extended.Cut", "Extended Cut")]
    [InlineData("My.Movie.Assembly.Cut.1992.REPACK.1080p.BluRay.DD5.1.x264-Group", "Assembly Cut")]
    [InlineData("Movie.1987.Ultimate.Hunter.Edition.DTS-HD.DTS.MULTISUBS.1080p.BluRay.x264.HQ-TUSAHD", "Ultimate Hunter Edition")]
    [InlineData("Movie.1950.Diamond.Edition.1080p.BluRay.x264-nikt0", "Diamond Edition")]
    [InlineData("Movie.Title.1990.Ultimate.Rekall.Edition.NORDiC.REMUX.1080p.BluRay.AVC.DTS-HD.MA5.1-TWA", "Ultimate Rekall Edition")]
    [InlineData("Movie.Title.1971.Signature.Edition.1080p.BluRay.FLAC.2.0.x264-TDD", "Signature Edition")]
    [InlineData("Movie.1979.The.Imperial.Edition.BluRay.720p.DTS.x264-CtrlHD", "Imperial Edition")]
    [InlineData("Movie.1997.Open.Matte.1080p.BluRay.x264.DTS-FGT", "Open Matte")]
    public void ParsesEdition(string postTitle, string edition)
    {
        Assert.Equal(edition, MovieTitleParser.ParseMovieTitle(postTitle)?.Edition);
    }

    // Upstream "" means no edition: modularr uses null.
    [Theory]
    [InlineData("Movie.Holiday.Special.1978.DVD.REMUX.DD.2.0-ViETNAM")]
    [InlineData("Directors.Cut.German.2006.COMPLETE.PAL.DVDR-LoD")]
    [InlineData("Movie Impossible: Rogue Movie 2012 Bluray")]
    [InlineData("Loving.Movie.2018.TS.FRENCH.MD.x264-DROGUERiE")]
    [InlineData("Uncut.Movie.2019.720p.BluRay.x264-YOL0W")]
    [InlineData("The.Christmas.Edition.1941.720p.HDTV.x264-CRiMSON")]
    public void DoesNotParseEdition(string postTitle)
    {
        ParsedMovieInfo? parsed = MovieTitleParser.ParseMovieTitle(postTitle);
        Assert.NotNull(parsed);
        Assert.Null(parsed.Edition);
    }
}
