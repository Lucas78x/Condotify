using CondotifyAPI.Services.CFTV;
using Xunit;

namespace CondotifyAPI.Tests;

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
