using Modularr.Core.Languages;
using Modularr.Core.Parsing;

namespace Modularr.Core.Tests.Parsing;

public sealed class ReleaseLanguagesTests
{
    // audio and subtitles: comma separated language names; "" for none.
    [Theory]
    [InlineData("Movie.Title.2020.VOSTFR.1080p.WEB.x264-GRP", "Unknown", "French")]
    [InlineData("[Team] Series Title - 05 [VOSTFR][1080p]", "Unknown", "French")]
    [InlineData("Movie.Title.2020.VOST-FR.1080p.WEB.x264-GRP", "Unknown", "French")]
    [InlineData("Movie.Title.2020.STFR.1080p.WEB.x264-GRP", "Unknown", "French")]
    [InlineData("Movie.Title.2010.SUBFRENCH.1080p.WEB.x264-GROUP", "Unknown", "French")]
    [InlineData("Series.Title.S01E01.SUBFRENCH.1080p.WEB.x264-GROUP", "Unknown", "French")]
    [InlineData("Movie.Title.2020.VOSTA.1080p.WEB.x264-GRP", "Unknown", "English")]
    [InlineData("Movie.Title.2020.VOSTENG.1080p.WEB.x264-GRP", "Unknown", "English")]
    [InlineData("Movie.Title.2020.VO.1080p.WEB.x264-GRP", "Original", "")]
    [InlineData("Movie.Title.2020.MULTi.VOSTFR.1080p.WEB.x264-GRP", "French,Original", "French")]
    [InlineData("Series Title S01 AVC.1080p.Blu-ray HD.VOSTFR.VFF", "French", "French")]
    [InlineData("Movie.Title.2020.Eng.Fre.Subs.1080p.WEB.x264-GRP", "Unknown", "English,French")]
    [InlineData("Movie.Title.2020.German.DL.French.Subs.1080p.WEB.x264-GRP", "German,Original", "French")]
    [InlineData("Movie.Title.2020.TRUEFRENCH.1080p.WEB.x264-GRP", "French", "")]
    [InlineData("Movie.Title.2020.1080p.WEB.x264-GRP", "Unknown", "")]
    public void SplitsAudioFromSubtitles(string title, string audio, string subtitles)
    {
        ReleaseLanguages parsed = LanguageParser.ParseReleaseLanguages(title);

        Assert.Equal(Names(audio), parsed.Audio.OrderBy(l => l.Id));
        Assert.Equal(Names(subtitles), parsed.Subtitles.OrderBy(l => l.Id));
    }

    [Theory]
    [InlineData("Movie.Title.2020.MULTi.1080p.BluRay.x264-GRP", true, false)] // bare MULTi: the French scene's VFF + VO
    [InlineData("Movie.Title.2020.MULTi.VFF.1080p.BluRay.x264-GRP", true, false)]
    [InlineData("Movie.Title.2020.TRUEFRENCH.1080p.BluRay.x264-GRP", true, false)]
    [InlineData("Movie.Title.2020.FRENCH.1080p.BluRay.x264-GRP", true, false)]
    [InlineData("Movie.Title.2020.VFQ.1080p.WEB.x264-GRP", false, true)]
    [InlineData("Movie.Title.2020.MULTi.VFQ.1080p.WEB.x264-GRP", false, true)]
    [InlineData("Movie.Title.2020.MULTi.VFF.VFQ.1080p.WEB.x264-GRP", true, true)]
    [InlineData("Movie.Title.2020.VOSTFR.1080p.WEB.x264-GRP", false, false)]
    public void TellsTheFrenchDubs(string title, bool france, bool quebec)
    {
        ReleaseLanguages parsed = LanguageParser.ParseReleaseLanguages(title);

        Assert.Equal((france, quebec), (parsed.FranceFrench, parsed.QuebecFrench));
    }

    [Theory]
    [InlineData("Movie.Title.2020.Eng.Fre.Multi.Subs.1080p.WEB.x264-GRP", true, false)]
    [InlineData("Movie.Title.2020.1080p.WEB.MULTISUB.x264-GRP", true, false)]
    [InlineData("Series Title S03 - EP14 VOSTFR [1080p] [HardSub] Yass'Kun", false, true)]
    [InlineData("Movie.Title.2020.HC.HDRip.x264-GRP", false, true)]
    [InlineData("Movie.Title.2020.MULTi.1080p.BluRay.x264-GRP", false, false)]
    public void FlagsMultiSubsAndHardsubs(string title, bool multiSubs, bool hardsub)
    {
        ReleaseLanguages parsed = LanguageParser.ParseReleaseLanguages(title);

        Assert.Equal((multiSubs, hardsub), (parsed.MultiSubs, parsed.Hardsub));
    }

    // "Multi Subs" names subtitles: no Original track is added.
    [Fact]
    public void MultiSubsIsNotMultiAudio() =>
        Assert.Equal([Language.English, Language.French], LanguageParser.ParseReleaseLanguages("Movie.Title.2020.Eng.Fre.Multi.Subs.1080p.WEB.x264-GRP").Audio.OrderBy(l => l.Id));

    private static IEnumerable<Language> Names(string names) =>
        names.Length == 0 ? [] : names.Split(',').Select(Language.FindByName).OrderBy(l => l.Id);
}
