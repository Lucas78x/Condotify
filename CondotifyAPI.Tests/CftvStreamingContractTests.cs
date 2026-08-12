using System.Text.Json;
using CondotifyAPI.Data.Equipments;
using Condotify.Models;
using Xunit;

namespace CondotifyAPI.Tests;

public class CftvStreamingContractTests
{
    [Fact]
    public void CftvSessionOut_NeverSerializesCredentialsOrRtspUrls()
    {
        var session = new CftvSessionOut(
            SessionId: Guid.NewGuid(),
            PlaybackUrl: "http://localhost:8889/l1_d2_c1/whep",
            Token: "token-opaco",
            ExpiresAt: DateTime.UtcNow.AddSeconds(120),
            Protocol: "webrtc");

        var json = JsonSerializer.Serialize(session);

        Assert.DoesNotContain("rtsp://", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("senha", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CftvSessionOut_ExposesOnlyTheFiveIntendedProperties()
    {
        var names = typeof(CftvSessionOut).GetProperties().Select(x => x.Name).OrderBy(x => x).ToArray();

        Assert.Equal(
            ["ExpiresAt", "PlaybackUrl", "Protocol", "SessionId", "Token"],
            names);
    }

    [Fact]
    public void CameraListContract_NeverExposesThePersistedPassword()
    {
        var names = typeof(CftvDeviceRowViewModel).GetProperties().Select(x => x.Name).ToArray();

        Assert.DoesNotContain(names, name => name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Senha", StringComparison.OrdinalIgnoreCase));
    }
}
