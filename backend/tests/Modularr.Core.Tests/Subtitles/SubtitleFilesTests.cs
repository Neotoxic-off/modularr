using System.Text;
using Modularr.Core.Languages;
using Modularr.Core.PostProcessing;
using Modularr.Core.Subtitles;
using Modularr.Core.Tests.PostProcessing;

namespace Modularr.Core.Tests.Subtitles;

public sealed class SubtitleFilesTests : IDisposable
{
    private static readonly byte[] Srt = Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nBonjour\n");

    private readonly TempDownload _library = new();

    public void Dispose() => _library.Dispose();

    private string[] Roots => [_library.PathOf("movies")];

    private string Video => _library.CreateFile("movies/Inception (2010)/Inception.2010.1080p.BluRay.x264-GRP.mkv");

    [Theory]
    [InlineData("French", false, false, ".srt", "Inception.2010.1080p.fr.srt")]
    [InlineData("English", true, false, ".SRT", "Inception.2010.1080p.en.hi.srt")]
    [InlineData("Portuguese (Brazil)", false, true, ".ass", "Inception.2010.1080p.pt.forced.ass")]
    [InlineData("German", true, true, ".vtt", "Inception.2010.1080p.de.hi.forced.vtt")]
    public void NamesFollowTheVideo(string language, bool hearingImpaired, bool forced, string extension, string expected) =>
        Assert.Equal(expected, SubtitleFiles.NameFor("/m/Inception.2010.1080p.mkv", Language.FindByName(language), hearingImpaired, forced, extension));

    [Theory]
    [InlineData(".exe")]
    [InlineData(".srt.exe")]
    [InlineData(".idx")]
    [InlineData("")]
    public void OnlySubtitleExtensionsAreNamed(string extension) =>
        Assert.Throws<ArgumentException>(() => SubtitleFiles.NameFor("/m/a.mkv", Language.French, false, false, extension));

    [Theory]
    [InlineData("subtitle.srt", 10, null)]
    [InlineData("subtitle.SSA", 10, null)]
    [InlineData("subtitle.sub", 10, null)]
    [InlineData("subtitle.exe", 10, "not an accepted subtitle type")]
    [InlineData("subtitle.txt", 10, "not an accepted subtitle type")]
    [InlineData("subtitle.srt", 0, "empty")]
    [InlineData("subtitle.srt", SubtitleFiles.MaxBytes, null)]
    [InlineData("subtitle.srt", SubtitleFiles.MaxBytes + 1, "larger than 2 MiB")]
    public void AProviderFileIsCheckedForTypeAndSize(string fileName, int size, string? error)
    {
        (string Extension, byte[] Content)? unpacked = SubtitleFiles.Unpack(fileName, new byte[size], new PostProcessingOptions(), out string? reason);

        Assert.Equal(error is null, unpacked is not null);
        if (error is not null)
        {
            Assert.Contains(error, reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnArchiveGivesItsSubtitle()
    {
        byte[] zip = TempDownload.ZipBytes(("readme.nfo", [1]), ("sub/Inception.FR.SRT", Srt));

        (string Extension, byte[] Content)? unpacked = SubtitleFiles.Unpack("Inception.zip", zip, new PostProcessingOptions(), out string? error);

        Assert.Null(error);
        Assert.Equal(".srt", unpacked!.Value.Extension);
        Assert.Equal(Srt, unpacked.Value.Content);
    }

    [Fact]
    public void AnArchiveWithoutSubtitleIsRefused()
    {
        byte[] zip = TempDownload.ZipBytes(("setup.exe", [1, 2, 3]));

        Assert.Null(SubtitleFiles.Unpack("subs.zip", zip, new PostProcessingOptions(), out string? error));
        Assert.Contains("no subtitle", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ASubtitleIsSavedOnceAndNeverOverwritten()
    {
        string video = Video;

        string? saved = SubtitleFiles.Save(video, Roots, Language.French, hearingImpaired: false, forced: false, ".srt", Srt);
        string? again = SubtitleFiles.Save(video, Roots, Language.French, hearingImpaired: false, forced: false, ".srt", [9]);

        Assert.Equal(Path.Combine(Path.GetDirectoryName(video)!, "Inception.2010.1080p.BluRay.x264-GRP.fr.srt"), saved);
        Assert.Null(again);
        Assert.Equal(Srt, File.ReadAllBytes(saved!));
    }

    [Fact]
    public void NothingIsWrittenOrReadOutsideTheLibrary()
    {
        string outside = _library.CreateFile("elsewhere/Movie.mkv");

        Assert.Throws<ArgumentException>(() => SubtitleFiles.Save(outside, Roots, Language.French, false, false, ".srt", Srt));
        Assert.Throws<ArgumentException>(() => SubtitleFiles.Present(outside, Roots));
        Assert.Throws<ArgumentException>(() => SubtitleFiles.Present(_library.PathOf("movies/../elsewhere/Movie.mkv"), Roots));
        Assert.False(File.Exists(_library.PathOf("elsewhere/Movie.fr.srt")));
    }

    [Fact]
    public void PresentSubtitlesAreReadFromTheirNames()
    {
        string video = Video;
        string folder = Path.GetDirectoryName(video)!;
        foreach (string name in new[] { "x264-GRP.fr.srt", "x264-GRP.en.hi.srt", "x264-GRP.eng.forced.ass", "x264-GRP.srt", "x264-GRP.fr.txt", "x264-GRP.zz.srt" })
        {
            File.WriteAllBytes(Path.Combine(folder, "Inception.2010.1080p.BluRay." + name), Srt);
        }

        File.WriteAllBytes(Path.Combine(folder, "Other.Movie.de.srt"), Srt);

        IReadOnlyList<SubtitleFile> present = SubtitleFiles.Present(video, Roots);

        Assert.Equal(
            [(Language.English, true, false), (Language.English, false, true), (Language.French, false, false)],
            present.Select(s => (s.Language, s.HearingImpaired, s.Forced)).OrderBy(s => s.Language.Id).ThenBy(s => s.Forced));
    }

    [Fact]
    public void TheMovieHashSumsSizeAndBothEnds()
    {
        byte[] content = new byte[3 * 64 * 1024];
        content[0] = 1; // first 64 KiB
        content[^8] = 2; // last 64 KiB
        content[100_000] = 0xFF; // the middle is not hashed
        string file = _library.Write("hash.bin", content);

        Assert.Equal((content.Length + 3L).ToString("x16", System.Globalization.CultureInfo.InvariantCulture), SubtitleFiles.MovieHash(file));
        Assert.Null(SubtitleFiles.MovieHash(_library.Write("small.bin", new byte[1000])));
    }
}
