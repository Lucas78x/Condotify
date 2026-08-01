using CondotifyAPI.Services.CFTV;
using Xunit;

namespace CondotifyAPI.Tests;

public class CftvStreamPathResolverTests
{
    private readonly CftvStreamPathResolver _resolver = new();

    // --- ConnectivityProbePaths: contrato de NAO-REGRESSAO ---
    // Estes valores sao os que CFTVService usa hoje contra hardware real.
    // Se um destes testes falhar apos uma alteracao, a alteracao esta errada.

    [Fact]
    public void ConnectivityProbePaths_ForAxisCamera_MatchesTodaysBehaviour()
    {
        var paths = _resolver.ConnectivityProbePaths(MarkEnum.Axis, CFTVDeviceTypeEnum.Camera, 1);

        Assert.Equal(["/axis-media/media.amp", "/axis-media/media.amp?videocodec=h264"], paths);
    }

    [Theory]
    [InlineData(MarkEnum.Intelbras)]
    [InlineData(MarkEnum.Dahua)]
    [InlineData(MarkEnum.Hikvision)]
    [InlineData(MarkEnum.Uniview)]
    [InlineData(MarkEnum.None)]
    public void ConnectivityProbePaths_ForNonAxisCamera_IsGeneric_AsToday(MarkEnum mark)
    {
        var paths = _resolver.ConnectivityProbePaths(mark, CFTVDeviceTypeEnum.Camera, 1);

        Assert.Equal(["/live", "/stream1", "/h264"], paths);
    }

    [Fact]
    public void ConnectivityProbePaths_ForHikvisionRecorder_SubstitutesChannel()
    {
        var paths = _resolver.ConnectivityProbePaths(MarkEnum.Hikvision, CFTVDeviceTypeEnum.DVR, 3);

        Assert.Equal(
        [
            "/Streaming/Channels/0301",
            "/Streaming/Channels/0302",
            "/h264/ch03/main/av_stream",
            "/h264/ch03/sub/av_stream"
        ], paths);
    }

    [Fact]
    public void ConnectivityProbePaths_ForUniviewRecorder_UsesTheLiveChFormat()
    {
        var paths = _resolver.ConnectivityProbePaths(MarkEnum.Uniview, CFTVDeviceTypeEnum.DVR, 2);

        Assert.Equal(["/live/ch02_0", "/live/ch02_1"], paths);
    }

    [Fact]
    public void ConnectivityProbePaths_ForAxisRecorder_HasNoChannelSubstitution()
    {
        var paths = _resolver.ConnectivityProbePaths(MarkEnum.Axis, CFTVDeviceTypeEnum.DVR, 5);

        Assert.Equal(["/axis-media/media.amp"], paths);
    }

    [Fact]
    public void ConnectivityProbePaths_ForUnknownRecorderBrand_UsesTheTwoEntryFallback()
    {
        var paths = _resolver.ConnectivityProbePaths(MarkEnum.None, CFTVDeviceTypeEnum.DVR, 7);

        Assert.Equal(["/cam/realmonitor?channel=07&subtype=0", "/Streaming/Channels/0701"], paths);
    }

    [Fact]
    public void ConnectivityProbePaths_ForNvr_UsesTheSameTableAsDvr()
    {
        var paths = _resolver.ConnectivityProbePaths(MarkEnum.Hikvision, CFTVDeviceTypeEnum.NVR, 3);

        Assert.Equal(
        [
            "/Streaming/Channels/0301",
            "/Streaming/Channels/0302",
            "/h264/ch03/main/av_stream",
            "/h264/ch03/sub/av_stream"
        ], paths);
    }

    // --- PreferredPath: comportamento NOVO, so para o gateway ---

    [Theory]
    [InlineData(MarkEnum.Intelbras, StreamQuality.Main, "subtype=0")]
    [InlineData(MarkEnum.Intelbras, StreamQuality.Secondary, "subtype=1")]
    [InlineData(MarkEnum.Hikvision, StreamQuality.Main, "/Streaming/Channels/101")]
    [InlineData(MarkEnum.Hikvision, StreamQuality.Secondary, "/Streaming/Channels/102")]
    [InlineData(MarkEnum.Uniview, StreamQuality.Main, "/live/0/main")]
    [InlineData(MarkEnum.Uniview, StreamQuality.Secondary, "/live/0/sub")]
    public void PreferredPath_ForCamera_UsesThePerBrandTable(
        MarkEnum mark, StreamQuality quality, string expectedFragment)
    {
        var path = _resolver.PreferredPath(mark, CFTVDeviceTypeEnum.Camera, 1, quality);

        Assert.NotNull(path);
        Assert.Contains(expectedFragment, path);
    }

    [Fact]
    public void PreferredPath_ReturnsNull_ForUnknownBrand()
    {
        Assert.Null(_resolver.PreferredPath(MarkEnum.None, CFTVDeviceTypeEnum.Camera, 1, StreamQuality.Main));
    }

    [Fact]
    public void PreferredPath_ForRecorder_SubstitutesChannel()
    {
        var path = _resolver.PreferredPath(MarkEnum.Hikvision, CFTVDeviceTypeEnum.DVR, 4, StreamQuality.Main);

        Assert.Equal("/Streaming/Channels/0401", path);
    }

    [Fact]
    public void BuildRtspUrl_EscapesCredentials()
    {
        var url = _resolver.BuildRtspUrl("10.0.0.1", 554, "user@dom", "p@ss word", "/live");

        Assert.StartsWith("rtsp://", url);
        Assert.Contains("10.0.0.1:554/live", url);
        Assert.DoesNotContain(" ", url);
    }
}

public class RtspUrlMaskerTests
{
    [Fact]
    public void Mask_RemovesCredentials_FromRtspUrl()
    {
        var masked = RtspUrlMasker.Mask("rtsp://admin:s3nh4Secreta@192.168.0.10:554/cam/realmonitor?channel=1&subtype=0");

        Assert.DoesNotContain("s3nh4Secreta", masked);
        Assert.DoesNotContain("admin", masked);
        Assert.Contains("192.168.0.10:554", masked);
        Assert.Contains("/cam/realmonitor", masked);
    }

    [Fact]
    public void Mask_HandlesUrlWithoutCredentials()
    {
        var masked = RtspUrlMasker.Mask("rtsp://192.168.0.10:554/live");

        Assert.Equal("rtsp://192.168.0.10:554/live", masked);
    }

    [Fact]
    public void Mask_HandlesEncodedCredentials()
    {
        var masked = RtspUrlMasker.Mask("rtsp://user%40dom:p%40ss@10.0.0.1:554/h264");

        Assert.DoesNotContain("p%40ss", masked);
        Assert.Contains("10.0.0.1:554/h264", masked);
    }

    [Fact]
    public void Mask_ReturnsPlaceholder_ForGarbageInput()
    {
        Assert.Equal("rtsp://***", RtspUrlMasker.Mask("nao-e-uma-url"));
    }
}
