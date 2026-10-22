using Modularr.Core.PostProcessing;

namespace Modularr.Core.Tests.PostProcessing;

public sealed class VideoSignatureTests
{
    public static TheoryData<string, byte[], bool> Headers => new()
    {
        { ".mkv", Header((0, "1A45DFA3")), true },
        { ".webm", Header((0, "1A45DFA3")), true },
        { ".mp4", Header((4, "66747970")), true },
        { ".mov", Header((4, "6D6F6F76")), true },
        { ".m4v", Header((4, "66747970")), true },
        { ".avi", Header((0, "52494646"), (8, "41564920")), true },
        { ".ts", Header((0, "47"), (188, "47")), true },
        { ".m2ts", Header((4, "47"), (196, "47")), true },
        { ".mpg", Header((0, "000001BA")), true },
        { ".vob", Header((0, "000001BA")), true },
        { ".m2v", Header((0, "000001B3")), true },
        { ".wmv", Header((0, "3026B2758E66CF11")), true },
        { ".flv", Header((0, "464C56")), true },
        { ".ogv", Header((0, "4F676753")), true },
        { ".rmvb", Header((0, "2E524D46")), true },
        { ".iso", Header((0x8001, "4344303031")), true },
        { ".iso", Header((0x8001, "4245413031")), true },
        { ".mp4", Header((0, "1A45DFA3")), true },
        { ".MKV", Header((0, "1A45DFA3")), true },
        { ".strm", Header((0, "4D5A")), true },
        { ".mkv", Header((0, "4D5A9000")), false },
        { ".avi", Header((0, "504B0304")), false },
        { ".avi", Header((0, "52494646"), (8, "57415645")), false },
        { ".ts", Header((0, "47")), false },
        { ".mkv", Header((0, "52617221")), false },
        { ".mp4", [], false },
        { ".mkv", "fake matroska payload\n"u8.ToArray(), false },
    };

    [Theory]
    [MemberData(nameof(Headers))]
    public void RecognisesVideoContainers(string extension, byte[] header, bool expected)
    {
        Assert.Equal(expected, VideoSignature.IsVideoContainer(extension, header));
    }

    [Fact]
    public void RejectsAVideoThatIsReallyAZip()
    {
        using TempDownload download = new();
        download.Zip("Show.S01E01.mkv");
        download.Write("Show.S01E01.avi", TempDownload.ZipBytes(("a.txt", [])));

        PostProcessingVerdict.Rejected rejected = Assert.IsType<PostProcessingVerdict.Rejected>(CompletedDownloadProcessor.Process(download.Root, new PostProcessingOptions()));

        Assert.True(rejected.Blacklist);
        Assert.Contains("content is not a video", rejected.Reason, StringComparison.Ordinal);
        Assert.Contains("Show.S01E01.mkv", rejected.Reason, StringComparison.Ordinal);
        Assert.Contains("Show.S01E01.avi", rejected.Reason, StringComparison.Ordinal);
        Assert.Empty(download.Files());
    }

    private static byte[] Header(params (int Offset, string Hex)[] parts)
    {
        byte[] header = new byte[VideoSignature.HeaderLength];
        foreach ((int offset, string hex) in parts)
        {
            Convert.FromHexString(hex).CopyTo(header, offset);
        }

        return header;
    }
}
