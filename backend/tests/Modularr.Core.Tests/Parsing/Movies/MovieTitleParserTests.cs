// Cases from the Radarr ParserFixture (GPL-3.0).
using Modularr.Core.Languages;
using Modularr.Core.Parsing.Movies;

namespace Modularr.Core.Tests.Parsing.Movies;

public sealed class MovieTitleParserTests
{
    [Theory]
    [InlineData("The.Italian.Movie.2025.720p.BluRay.X264-AMIABLE")]
    [InlineData("The.French.Movie.2013.720p.BluRay.x264 - ROUGH[PublicHD]")]
    [InlineData("The.German.Doctor.2013.LIMITED.DVDRip.x264-RedBlade")]
    [InlineData("The.Good.German.2006.720p.HDTV.x264-TVP")]
    [InlineData("German.Lancers.2019.720p.BluRay.x264-UNiVERSUM")]
    [InlineData("The.German.2019.720p.BluRay.x264-UNiVERSUM")]
    [InlineData("Movie Name (2016) BluRay 1080p DTS-ES AC3 x264-3Li")]
    public void DoesNotParseWrongLanguageInTitle(string postTitle)
    {
        Assert.Equal(Language.Unknown, Assert.Single(Parse(postTitle, isDir: true).Languages));
    }

    // From the Radarr LanguageParserFixture: the movie name and release group must be hidden before language parsing.
    [Theory]
    [InlineData("The Danish Movie 2015")]
    [InlineData("Movie.Title.2010.720p.BluRay.x264.-[YTS.LT]")]
    public void ParsesUnknownLanguageOutsideTitleAndGroup(string postTitle)
    {
        Assert.Equal(Language.Unknown, Assert.Single(Parse(postTitle).Languages));
    }

    private static ParsedMovieInfo Parse(string title, bool isDir = false)
    {
        ParsedMovieInfo? parsed = MovieTitleParser.ParseMovieTitle(title, isDir);
        Assert.NotNull(parsed);
        return parsed;
    }

    [Fact]
    public void RemovesAccentsFromTitle()
    {
        Assert.Equal("carnivale", MovieTitleParser.CleanMovieTitle("Carnivàle"));
    }

    // Sonarr's generic 24-39 character hash rules would reject a long single-word movie title; Radarr's list does not.
    [Fact]
    public void ParsesLongSingleWordTitle()
    {
        ParsedMovieInfo? parsed = MovieTitleParser.ParseMovieTitle("Thisisaverylongsinglewordtitle 2019 1080p BluRay x264-GRP");

        Assert.NotNull(parsed);
        Assert.Equal("Thisisaverylongsinglewordtitle", parsed.PrimaryMovieTitle);
    }

    [Theory]
    [InlineData("The.Movie.from.U.N.C.L.E.2015.1080p.BluRay.x264-SPARKS", "The Movie from U.N.C.L.E.")]
    [InlineData("1776.1979.EXTENDED.720p.BluRay.X264-AMIABLE", "1776")]
    [InlineData("MY MOVIE (2016) [R][Action, Horror][720p.WEB-DL.AVC.8Bit.6ch.AC3].mkv", "MY MOVIE")]
    [InlineData("R.I.P.D.2013.720p.BluRay.x264-SPARKS", "R.I.P.D.")]
    [InlineData("V.H.S.2.2013.LIMITED.720p.BluRay.x264-GECKOS", "V.H.S. 2")]
    [InlineData("This Is A Movie (1999) [IMDB #] <Genre, Genre, Genre> {ACTORS} !DIRECTOR +MORE_SILLY_STUFF_NO_ONE_NEEDS ?", "This Is A Movie")]
    [InlineData("We Are the Movie!.2013.720p.H264.mkv", "We Are the Movie!")]
    [InlineData("(500).Days.Of.Movie.(2009).DTS.1080p.BluRay.x264.NLsubs", "(500) Days Of Movie")]
    [InlineData("To.Live.and.Movie.in.L.A.1985.1080p.BluRay", "To Live and Movie in L.A.")]
    [InlineData("A.I.Artificial.Movie.(2001)", "A.I. Artificial Movie")]
    [InlineData("A.Movie.Name.(1998)", "A Movie Name")]
    [InlineData("www.Torrenting.com - Movie.2008.720p.X264-DIMENSION", "Movie")]
    [InlineData("www.5MovieRulz.tc - Movie (2000) Malayalam HQ HDRip - x264 - AAC - 700MB.mkv", "Movie")]
    [InlineData("Movie: The Movie World 2013", "Movie: The Movie World")]
    [InlineData("Movie.The.Final.Chapter.2016", "Movie The Final Chapter")]
    [InlineData("Der.Movie.James.German.Bluray.FuckYou.Pso.Why.cant.you.follow.scene.rules.1998", "Der Movie James")]
    [InlineData("Movie.German.DL.AC3.Dubbed..BluRay.x264-PsO", "Movie")]
    [InlineData("Valana la Movie TRUEFRENCH BluRay 720p 2016 kjhlj", "Valana la Movie")]
    [InlineData("Mission Movie: Rogue Movie (2015)�[XviD - Ita Ac3 - SoftSub Ita]azione, spionaggio, thriller *Prima Visione* Team mulnic Tom Cruise", "Mission Movie: Rogue Movie")]
    [InlineData("Movie.Movie.2000.FRENCH..BluRay.-AiRLiNE", "Movie Movie")]
    [InlineData("My Movie 1999 German Bluray", "My Movie")]
    [InlineData("Leaving Movie by Movie (1897) [DVD].mp4", "Leaving Movie by Movie")]
    [InlineData("Movie.2018.1080p.AMZN.WEB-DL.DD5.1.H.264-NTG", "Movie")]
    [InlineData("Movie.Title.Imax.2018.1080p.AMZN.WEB-DL.DD5.1.H.264-NTG", "Movie Title")]
    [InlineData("World.Movie.Z.EXTENDED.2013.German.DL.1080p.BluRay.AVC-XANOR", "World Movie Z")]
    [InlineData("World.Movie.Z.2.EXTENDED.2013.German.DL.1080p.BluRay.AVC-XANOR", "World Movie Z 2")]
    [InlineData("G.I.Movie.Movie.2013.THEATRiCAL.COMPLETE.BLURAY-GLiMMER", "G.I. Movie Movie")]
    [InlineData("www.Torrenting.org - Movie.2008.720p.X264-DIMENSION", "Movie")]
    [InlineData("The.French.Movie.2013.720p.BluRay.x264 - ROUGH[PublicHD]", "The French Movie")]
    // Hardcoded to exclude from German regex
    [InlineData("The.Good.German.2006.720p.BluRay.x264-RlsGrp", "The Good German")]
    public void ParsesMovieTitle(string postTitle, string title)
    {
        Assert.Equal(title, Parse(postTitle).PrimaryMovieTitle);
    }

    [Theory]
    [InlineData("[MTBB] Kimi no Na wa. (2016) v2 [97681524].mkv", "Kimi no Na wa", "MTBB", 2016)]
    [InlineData("[sam] Toward the Terra (1980) [BD 1080p TrueHD].mkv", "Toward the Terra", "sam", 1980)]
    [InlineData("[Inka-Subs] Rokudenashi Blues 1993 (1993) [VHS]", "Rokudenashi Blues 1993", "Inka-Subs", 1993)]
    public void ParsesAnimeMovieTitle(string postTitle, string title, string releaseGroup, int year)
    {
        ParsedMovieInfo movie = Parse(postTitle);
        Assert.Equal(title, movie.PrimaryMovieTitle);
        Assert.Equal(releaseGroup, movie.ReleaseGroup);
        Assert.Equal(year, movie.Year);
    }

    [Theory]
    [InlineData("[Arid] Cowboy Bebop - Knockin' on Heaven's Door v2 [00F4CDA0].mkv", "Cowboy Bebop - Knockin' on Heaven's Door", "Arid")]
    [InlineData("[Baws] Evangelion 1.11 - You Are (Not) Alone v2 (1080p BD HEVC FLAC) [BF42B1C8].mkv", "Evangelion 1 11 - You Are (Not) Alone", "Baws")]
    [InlineData("[Arid] 5 Centimeters per Second (BDRip 1920x1080 Hi10 FLAC) [FD8B6FF2].mkv", "5 Centimeters per Second", "Arid")]
    [InlineData("[Baws] Evangelion 2.22 - You Can (Not) Advance (1080p BD HEVC FLAC) [56E7A5B8].mkv", "Evangelion 2 22 - You Can (Not) Advance", "Baws")]
    [InlineData("[sam] Goblin Slayer - Goblin's Crown [BD 1080p FLAC] [CD298D48].mkv", "Goblin Slayer - Goblin's Crown", "sam")]
    [InlineData("[Kulot] Violet Evergarden Gaiden Eien to Jidou Shuki Ningyou [Dual-Audio][BDRip 1920x804 HEVC FLACx2] [91FC62A8].mkv", "Violet Evergarden Gaiden Eien to Jidou Shuki Ningyou", "Kulot")]
    public void ParsesAnimeMovieTitleWithoutYear(string postTitle, string title, string releaseGroup)
    {
        ParsedMovieInfo movie = Parse(postTitle);
        Assert.Equal(title, movie.PrimaryMovieTitle);
        Assert.Equal(releaseGroup, movie.ReleaseGroup);
    }

    // Upstream "" edition and 0 year mean none: modularr uses null.
    [Theory]
    [InlineData("Movie.Aufbruch.nach.Pandora.Extended.2009.German.DTS.720p.BluRay.x264-SoW", "Movie Aufbruch nach Pandora", "Extended", 2009)]
    [InlineData("Drop.Movie.1994.German.AC3D.DL.720p.BluRay.x264-KLASSiGERHD", "Drop Movie", "", 1994)]
    [InlineData("Kick.Movie.2.2013.German.DTS.DL.720p.BluRay.x264-Pate", "Kick Movie 2", "", 2013)]
    [InlineData("Movie.Hills.2019.German.DL.AC3.Dubbed.1080p.BluRay.x264-muhHD", "Movie Hills", "", 2019)]
    [InlineData("96.Hours.Movie.3.EXTENDED.2014.German.DL.1080p.BluRay.x264-ENCOUNTERS", "96 Hours Movie 3", "EXTENDED", 2014)]
    [InlineData("Movie.War.Q.EXTENDED.CUT.2013.German.DL.1080p.BluRay.x264-HQX", "Movie War Q", "EXTENDED CUT", 2013)]
    [InlineData("Sin.Movie.2005.RECUT.EXTENDED.German.DL.1080p.BluRay.x264-DETAiLS", "Sin Movie", "RECUT EXTENDED", 2005)]
    [InlineData("2.Movie.in.L.A.1996.GERMAN.DL.720p.WEB.H264-SOV", "2 Movie in L.A.", "", 1996)]
    [InlineData("8.2019.GERMAN.720p.BluRay.x264-UNiVERSUM", "8", "", 2019)]
    [InlineData("Life.Movie.2014.German.DL.PAL.DVDR-ETM", "Life Movie", "", 2014)]
    [InlineData("Joe.Movie.2.EXTENDED.EDITION.2015.German.DL.PAL.DVDR-ETM", "Joe Movie 2", "EXTENDED EDITION", 2015)]
    [InlineData("Movie.EXTENDED.2011.HDRip.AC3.German.XviD-POE", "Movie", "EXTENDED", 2011)]
    // year in the title
    [InlineData("Movie.Klasse.von.1999.1990.German.720p.HDTV.x264-NORETAiL", "Movie Klasse von 1999", "", 1990)]
    // edition after year
    [InlineData("Movie.Squad.2016.EXTENDED.German.DL.AC3.BDRip.x264-hqc", "Movie Squad", "EXTENDED", 2016)]
    // edition after year
    [InlineData("Movie.and.Movie.2010.Extended.Cut.German.DTS.DL.720p.BluRay.x264-HDS", "Movie and Movie", "Extended Cut", 2010)]
    // year at the end
    [InlineData("Der.Movie.James.German.Bluray.FuckYou.Pso.Why.cant.you.follow.scene.rules.1998", "Der Movie James", "", 1998)]
    // no year & edition
    [InlineData("Der.Movie.Eine.Unerwartete.Reise.Extended.German.720p.BluRay.x264-EXQUiSiTE", "Der Movie Eine Unerwartete Reise", "Extended", 0)]
    // no year & edition
    [InlineData("Movie.Weg.des.Kriegers.EXTENDED.German.720p.BluRay.x264-EXQUiSiTE", "Movie Weg des Kriegers", "EXTENDED", 0)]
    // no year & edition
    [InlineData("Die.Unfassbaren.Movie.Name.EXTENDED.German.DTS.720p.BluRay.x264-RHD", "Die Unfassbaren Movie Name", "EXTENDED", 0)]
    // no year & edition & without dots
    [InlineData("Die Unfassbaren Movie Name EXTENDED German DTS 720p BluRay x264-RHD", "Die Unfassbaren Movie Name", "EXTENDED", 0)]
    // no year
    [InlineData("Passengers.German.DL.AC3.Dubbed..BluRay.x264-PsO", "Passengers", "", 0)]
    // no year
    [InlineData("Das.A.Team.Der.Film.Extended.Cut.German.720p.BluRay.x264-ANCIENT", "Das A Team Der Film", "Extended Cut", 0)]
    // no year
    [InlineData("Cars.2.German.DL.720p.BluRay.x264-EmpireHD", "Cars 2", "", 0)]
    // dot after dr
    [InlineData("Die.fantastische.Reise.des.Dr.Dolittle.2020.German.DL.LD.1080p.WEBRip.x264-PRD", "Die fantastische Reise des Dr. Dolittle", "", 2020)]
    // year at wrong position
    [InlineData("Der.Film.deines.Lebens.German.2011.PAL.DVDR-ETM", "Der Film deines Lebens", "", 2011)]
    // underscore at the end
    [InlineData("Kick.Ass.2.2013.German.DTS.DL.720p.BluRay.x264-Pate_", "Kick Ass 2", "", 2013)]
    // German in the title
    [InlineData("The.Good.German.2006.GERMAN.720p.HDTV.x264-RLsGrp", "The Good German", "", 2006)]
    public void ParsesGermanMovie(string postTitle, string title, string edition, int year)
    {
        ParsedMovieInfo movie = Parse(postTitle);
        Assert.Equal(title, movie.PrimaryMovieTitle);
        Assert.Equal(edition, movie.Edition ?? string.Empty);
        Assert.Equal(year, movie.Year ?? 0);
    }

    [Theory]
    [InlineData("L'hypothèse.du.movie.volé.AKA.The.Hypothesis.of.the.Movie.Title.1978.1080p.CINET.WEB-DL.AAC2.0.x264-Cinefeel.mkv", new[] { "L'hypothèse du movie volé AKA The Hypothesis of the Movie Title", "L'hypothèse du movie volé", "The Hypothesis of the Movie Title" })]
    [InlineData("Skjegg.AKA.Rox.Beard.1965.CD1.CRiTERiON.DVDRip.XviD-KG.avi", new[] { "Skjegg AKA Rox Beard", "Skjegg", "Rox Beard" })]
    [InlineData("Kjeller.chitai.AKA.Basement.of.Shame.1956.1080p.BluRay.x264.FLAC.1.0.mkv", new[] { "Kjeller chitai AKA Basement of Shame", "Kjeller chitai", "Basement of Shame" })]
    [InlineData("Radarr.Under.Water.(aka.Beneath.the.Code.Freeze).1997.DVDRip.x264.CG-Grzechsin.mkv", new[] { "Radarr Under Water (aka Beneath the Code Freeze)", "Radarr Under Water", "Beneath the Code Freeze" })]
    [InlineData("Radarr.prodavet. AKA.Radarr.Shift.2005.DVDRip.x264-HANDJOB.mkv", new[] { "Radarr prodavet  AKA Radarr Shift", "Radarr prodavet", "Radarr Shift" })]
    [InlineData("AKA.2002.DVDRip.x264-HANDJOB.mkv", new[] { "AKA" })]
    [InlineData("KillRoyWasHere.2000.BluRay.1080p.DTS.x264.dxva-EuReKA.mkv", new[] { "KillRoyWasHere" })]
    [InlineData("Aka Rox (2008).avi", new[] { "Aka Rox" })]
    [InlineData("Return Earth to Normal 'em High aka World 2 (2022) 1080p.mp4", new[] { "Return Earth to Normal 'em High aka World 2", "Return Earth to Normal 'em High", "World 2" })]
    [InlineData("Енола Голмс / Enola Holmes (2020) UHD WEB-DL 2160p 4K HDR H.265 Ukr/Eng | Sub Ukr/Eng", new[] { "Енола Голмс / Enola Holmes", "Енола Голмс", "Enola Holmes" })]
    [InlineData("Mon cousin a.k.a. My Cousin 2020 1080p Blu-ray DD 5.1 x264.mkv", new[] { "Mon cousin AKA My Cousin", "Mon cousin", "My Cousin" })]
    [InlineData("Sydney A.K.A. Hard Eight 1996 1080p AMZN WEB-DL DD+ 2.0 H.264.mkv", new[] { "Sydney AKA Hard Eight", "Sydney", "Hard Eight" })]
    public void ParsesMovieAlternativeTitles(string postTitle, string[] parsedTitles)
    {
        Assert.Equal(parsedTitles, Parse(postTitle, isDir: true).MovieTitles);
    }

    [Theory]
    [InlineData("(1995) Movie Name", "Movie Name")]
    public void ParsesMovieFolderName(string postTitle, string title)
    {
        Assert.Equal(title, Parse(postTitle, isDir: true).PrimaryMovieTitle);
    }

    [Theory]
    [InlineData("1776.1979.EXTENDED.720p.BluRay.X264-AMIABLE", 1979)]
    [InlineData("Movie Name FRENCH BluRay 720p 2016 kjhlj", 2016)]
    [InlineData("Der.Movie.German.Bluray.FuckYou.Pso.Why.cant.you.follow.scene.rules.1998", 1998)]
    [InlineData("Movie Name (1897) [DVD].mp4", 1897)]
    [InlineData("World Movie Z Movie [2023]", 2023)]
    public void ParsesMovieYear(string postTitle, int year)
    {
        Assert.Equal(year, Parse(postTitle).Year);
    }

    [Theory]
    [InlineData("Movie Name (2016) {tmdbid-43074}", 43074)]
    [InlineData("Movie Name (2016) [tmdb-43074]", 43074)]
    [InlineData("Movie Name (2016) {tmdb-43074}", 43074)]
    [InlineData("Movie Name (2016) {tmdb-2020}", 2020)]
    public void ParsesTmdbId(string postTitle, int tmdbId)
    {
        Assert.Equal(tmdbId, Parse(postTitle).TmdbId);
    }

    [Theory]
    [InlineData("Movie.Title.2016.1080p.KORSUB.WEBRip.x264.AAC2.0-RADARR", "KORSUB")]
    [InlineData("Movie.Title.2016.1080p.KORSUBS.WEBRip.x264.AAC2.0-RADARR", "KORSUBS")]
    [InlineData("Movie Title 2017 HC 720p HDRiP DD5 1 x264-LEGi0N", "Generic Hardcoded Subs")]
    [InlineData("Movie.Title.2017.720p.SUBBED.HDRip.V2.XViD-26k.avi", "Generic Hardcoded Subs")]
    [InlineData("Movie.Title.2000.1080p.BlueRay.x264.DTS.RoSubbed-playHD", null)]
    [InlineData("Movie Title! 2018 [Web][MKV][h264][480p][AAC 2.0][Softsubs]", null)]
    [InlineData("Movie Title! 2019 [HorribleSubs][Web][MKV][h264][848x480][AAC 2.0][Softsubs(HorribleSubs)]", null)]
    [InlineData("Movie Title! 2024 [Web][x265][1080p][EAC3][MultiSubs]", null)]
    public void ParsesHardcodedSubs(string postTitle, string? sub)
    {
        Assert.Equal(sub, Parse(postTitle).HardcodedSubs);
    }

    [Theory]
    [InlineData("That Italian Movie 2008 [tt1234567] 720p BluRay X264", "tt1234567")]
    [InlineData("That Italian Movie 2008 [tt12345678] 720p BluRay X264", "tt12345678")]
    public void ParsesImdbIdInTitle(string postTitle, string imdb)
    {
        Assert.Equal(imdb, Parse(postTitle, isDir: true).ImdbId);
    }

    [Theory]
    [InlineData("asfd", null)]
    [InlineData("123", "tt0000123")]
    [InlineData("1234567", "tt1234567")]
    [InlineData("tt1234567", "tt1234567")]
    [InlineData("tt12345678", "tt12345678")]
    [InlineData("12345678", "tt12345678")]
    public void NormalizesImdbId(string imdbId, string? normalized)
    {
        Assert.Equal(normalized, MovieTitleParser.NormalizeImdbId(imdbId));
    }

    // Not upstream: Radarr reversed titles into "System.Char[]", this checks the fixed reversal.
    [Fact]
    public void ParsesReversedTitle()
    {
        ParsedMovieInfo movie = Parse("PSA-462x.p0801.yaR-ulB.8102.eltiT.eivoM.mkv");
        Assert.Equal("Movie Title", movie.PrimaryMovieTitle);
        Assert.Equal(2018, movie.Year);
    }

    [Fact]
    public void ParsesReleaseHash()
    {
        Assert.Equal("BF42B1C8", Parse("[Baws] Evangelion 1.11 - You Are (Not) Alone v2 (1080p BD HEVC FLAC) [BF42B1C8].mkv").ReleaseHash);
    }
}
