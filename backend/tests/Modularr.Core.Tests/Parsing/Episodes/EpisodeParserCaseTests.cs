using Modularr.Core.Languages;
using Modularr.Core.Parsing;
using Modularr.Core.Parsing.Episodes;
using Modularr.Core.Qualities;

namespace Modularr.Core.Tests.Parsing.Episodes;

// Cases from the Sonarr ParserFixture (GPL-3.0).
public sealed class EpisodeParserCaseTests
{
    [Theory]
    [InlineData("Series Title - 4x05 - Title", "seriestitle")]
    [InlineData("Series & Title - 4x05 - Title", "seriestitle")]
    [InlineData("Bad Format", "badformat")]
    [InlineData("Mad Series - Season 1 [Bluray720p]", "madseries")]
    [InlineData("Mad Series - Season 1 [Bluray1080p]", "madseries")]
    [InlineData("The Daily Series -", "thedailyseries")]
    [InlineData("The Series Bros. (2006)", "theseriesbros2006")]
    [InlineData("Series (2011)", "series2011")]
    [InlineData("Series Time S02 720p HDTV x264 CRON", "seriestime")]
    [InlineData("Series Title 0", "seriestitle0")]
    [InlineData("Series of the Day", "seriesday")]
    [InlineData("Series of the Day 2", "seriesday2")]
    [InlineData("[ www.Torrenting.com ] - Series.S03E14.720p.HDTV.X264-DIMENSION", "series")]
    [InlineData("www.Torrenting.com - Series.S03E14.720p.HDTV.X264-DIMENSION", "series")]
    [InlineData("Series S02E09 HDTV x264-2HD [eztv]-[rarbg.com]", "series")]
    [InlineData("Series.911.S01.DVDRip.DD2.0.x264-DEEP", "series 911")]
    [InlineData("www.Torrenting.org - Series.S03E14.720p.HDTV.X264-DIMENSION", "series")]
    public void ShouldParseSeriesName(string postTitle, string title)
    {
        Assert.Equal(ReleaseTitle.CleanTitle(title), ReleaseTitle.CleanTitle(EpisodeParser.ParseSeriesName(postTitle)));
    }

    [Theory]
    [InlineData("Series S03E14 720p HDTV X264-DIMENSION", "Series")]
    [InlineData("Series.S03E14.720p.HDTV.X264-DIMENSION", "Series")]
    [InlineData("Series-S03E14-720p-HDTV-X264-DIMENSION", "Series")]
    [InlineData("Series_S03E14_720p_HDTV_X264-DIMENSION", "Series")]
    [InlineData("Series 2022 S03E14 720p HDTV X264-DIMENSION", "Series", 2022)]
    [InlineData("Series (2022) S03E14 720p HDTV X264-DIMENSION", "Series", 2022)]
    [InlineData("Series.2022.S03E14.720p.HDTV.X264-DIMENSION", "Series", 2022)]
    [InlineData("Series-2022-S03E14-720p-HDTV-X264-DIMENSION", "Series", 2022)]
    [InlineData("Series_2022_S03E14_720p_HDTV_X264-DIMENSION", "Series", 2022)]
    [InlineData("1234 S03E14 720p HDTV X264-DIMENSION", "1234")]
    [InlineData("1234.S03E14.720p.HDTV.X264-DIMENSION", "1234")]
    [InlineData("1234-S03E14-720p-HDTV-X264-DIMENSION", "1234")]
    [InlineData("1234_S03E14_720p_HDTV_X264-DIMENSION", "1234")]
    [InlineData("1234 2022 S03E14 720p HDTV X264-DIMENSION", "1234", 2022)]
    [InlineData("1234 (2022) S03E14 720p HDTV X264-DIMENSION", "1234", 2022)]
    [InlineData("1234.2022.S03E14.720p.HDTV.X264-DIMENSION", "1234", 2022)]
    [InlineData("1234-2022-S03E14-720p-HDTV-X264-DIMENSION", "1234", 2022)]
    [InlineData("1234_2022_S03E14_720p_HDTV_X264-DIMENSION", "1234", 2022)]
    public void ShouldParseSeriesTitleInfo(string postTitle, string titleWithoutYear, int year = 0)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(postTitle);
        Assert.NotNull(result);
        Assert.Equal(titleWithoutYear, result.SeriesTitleInfo.TitleWithoutYear);
        Assert.Equal(year, result.SeriesTitleInfo.Year);
    }

    [Theory]
    [InlineData("Sonar TV - Series Title : 02 Road From Code [S04].mp4")]
    public void ShouldCleanUpInvalidPathCharacters(string postTitle)
    {
        Assert.Null(Record.Exception(() => EpisodeParser.ParseTitle(postTitle)));
    }

    [Theory]
    [InlineData("[scnzbefnet][509103] 2.Developers.Series.S03E18.720p.HDTV.X264-DIMENSION", "2 Developers Series")]
    public void ShouldRemoveRequestInfoFromTitle(string postTitle, string title)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(postTitle);
        Assert.NotNull(result);
        Assert.Equal(title, result.SeriesTitle);
    }

    [Theory]
    [InlineData("Series.S01E02.Chained.Title.mkv")]
    [InlineData("Show - S01E01 - Title.avi")]
    public void ShouldParseQualityFromExtension(string title)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(title);
        Assert.NotNull(result);
        Assert.NotEqual(Quality.Unknown, result.Quality.Quality);
        Assert.Equal(QualityDetectionSource.Extension, result.Quality.SourceDetectionSource);
        Assert.Equal(QualityDetectionSource.Extension, result.Quality.ResolutionDetectionSource);
    }

    [Theory]
    [InlineData("Series.S01E02.Chained.Title.mkv", "Series.S01E02.Chained.Title")]
    public void ShouldParseReleasetitle(string path, string releaseTitle)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(path);
        Assert.NotNull(result);
        Assert.Equal(releaseTitle, result.ReleaseTitle);
    }

    [Theory]
    [InlineData("Босх: Спадок (S2E1) / Series: Legacy (S2E1) (2023) WEB-DL 1080p Ukr/Eng | sub Eng", new[] { "Босх: Спадок", "Series: Legacy" })]
    [InlineData("Босх: Спадок / Series: Legacy / S2E1-4 of 10 (2023) WEB-DL 1080p Ukr/Eng | sub Eng", new[] { "Босх: Спадок", "Series: Legacy" })]
    [InlineData("Босх: Спадок AKA Series: Legacy S02 1080p NF WEB-DL Dual- Audio DD+ 5.1 Atmos H.264-APEX", new[] { "Босх: Спадок", "Series: Legacy" })]
    [InlineData("Босх.Спадок.AKA.Series.Legacy.S02.1080p.NF.WEB-DL.DUAL.DDP5.1.Atmos.H.264-APEX", new[] { "Босх Спадок", "Series Legacy" })]
    [InlineData("[Judas] Series E (Series J) (Season 04) [1080p][HEVC x265 10bit][Dual-Audio][Multi-Subs] (Batch)", new[] { "Series E", "Series J" })]
    [InlineData("Serie / Series [S01E02] [MULTI] [1080p] [HMAX] [WEB-DL] [H.264] [DDP5.1.Atmos-K83] [Lektor i Napisy PL]", new[] { "Serie", "Series" })]
    public void ShouldParseMultipleSeriesTitles(string postTitle, string[] titles)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(postTitle);
        Assert.NotNull(result);
        Assert.Equal(titles, result.SeriesTitleInfo.AllTitles);
    }

    [Theory]
    [InlineData("[Reza] Series in Russian - S01E08 [WEBRip 1080p HEVC AAC] (Dual Audio) (Tokidoki Bosotto Russiago de Dereru Tonari no Alya-san)", "Unknown")]
    public void ShouldParseLanguageAfterParsingTitle(string postTitle, string expectedLanguage)
    {
        ParsedEpisodeInfo? result = EpisodeParser.ParseTitle(postTitle);
        Assert.NotNull(result);
        Assert.Equal(Language.FindByName(expectedLanguage), Assert.Single(result.Languages));
    }
}
