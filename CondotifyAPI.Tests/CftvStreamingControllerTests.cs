using CondotifyAPI.Controllers;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Services.CFTV;
using CondotifyAPI.Domain.DTO.Equipments;
using Xunit;

namespace CondotifyAPI.Tests;

// So a logica pura de OpenSession (sem banco, sem HTTP) e testada aqui, no mesmo
// espirito de CftvHealthMonitoringWorkerTests: os metodos estaticos internos sao a
// unica parte do controlador que nao depende de infraestrutura.
public class CftvStreamingControllerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(999999)]
    [InlineData(0)]
    [InlineData(-5)]
    public void ResolveChannel_AlwaysClampsCamerasToChannelOne(int requestedChannel)
    {
        // Camera nao tem "canais": PreferredPath ignora o canal para DeviceType.Camera e
        // sempre resolve para a mesma origem RTSP. Sem este clamp, canal:1 e canal:999999
        // viram dois caminhos MediaMTX distintos para a mesma camera, deixando um unico
        // usuario autorizado esgotar sozinho o limite de visualizadores da licenca.
        Assert.Equal(1, CftvStreamingController.ResolveChannel(CFTVDeviceTypeEnum.Camera, requestedChannel));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public void ResolveChannel_ForNonCameras_KeepsTheRequestedChannel_ButNeverBelowOne(int requested, int expected)
    {
        Assert.Equal(expected, CftvStreamingController.ResolveChannel(CFTVDeviceTypeEnum.DVR, requested));
    }

    [Fact]
    public void ChannelAvailability_RejectsDisabledOrUnknownChannels()
    {
        var channels = new[]
        {
            new CFTVChannelDTO { ChannelNumber = 1, IsEnabled = true },
            new CFTVChannelDTO { ChannelNumber = 2, IsEnabled = false }
        };

        Assert.True(CftvStreamingController.IsChannelEnabled(channels, 1));
        Assert.False(CftvStreamingController.IsChannelEnabled(channels, 2));
        Assert.False(CftvStreamingController.IsChannelEnabled(channels, 3));
    }

    [Fact]
    public void ResidentChannelVisibility_RequiresEnabledAndExplicitlySharedChannel()
    {
        var channels = new[]
        {
            new CFTVChannelDTO { ChannelNumber = 1, IsEnabled = true, ResidentVisible = true },
            new CFTVChannelDTO { ChannelNumber = 2, IsEnabled = true, ResidentVisible = false },
            new CFTVChannelDTO { ChannelNumber = 3, IsEnabled = false, ResidentVisible = true }
        };

        Assert.True(ResidentCftvController.IsResidentChannelVisible(channels, 1));
        Assert.False(ResidentCftvController.IsResidentChannelVisible(channels, 2));
        Assert.False(ResidentCftvController.IsResidentChannelVisible(channels, 3));
    }

    [Fact]
    public void IsCameraOffline_IsFalse_WhenTheDeviceIsActive()
    {
        Assert.False(CftvStreamingController.IsCameraOffline(isActive: true, lastSeenAt: null, utcNow: DateTime.UtcNow));
    }

    [Fact]
    public void IsCameraOffline_IsTrue_WhenInactiveAndNeverSeen()
    {
        Assert.True(CftvStreamingController.IsCameraOffline(isActive: false, lastSeenAt: null, utcNow: DateTime.UtcNow));
    }

    [Fact]
    public void IsCameraOffline_IsTrue_WhenInactiveAndLastSeenOverTenMinutesAgo()
    {
        var now = DateTime.UtcNow;
        Assert.True(CftvStreamingController.IsCameraOffline(isActive: false, lastSeenAt: now.AddMinutes(-11), utcNow: now));
    }

    [Fact]
    public void IsCameraOffline_IsFalse_WhenInactiveButSeenWithinTenMinutes()
    {
        // IsActive so vira false na proxima passagem do CftvHealthMonitoringWorker; um
        // LastSeenAt recente evita marcar como offline uma camera que apenas esta entre
        // duas checagens de saude.
        var now = DateTime.UtcNow;
        Assert.False(CftvStreamingController.IsCameraOffline(isActive: false, lastSeenAt: now.AddMinutes(-9), utcNow: now));
    }

    [Theory]
    [InlineData(MarkEnum.Hikvision, "/ISAPI/Streaming/channels/201/picture")]
    [InlineData(MarkEnum.Hilook, "/ISAPI/Streaming/channels/201/picture")]
    [InlineData(MarkEnum.Dahua, "/cgi-bin/snapshot.cgi?channel=2")]
    [InlineData(MarkEnum.Intelbras, "/cgi-bin/snapshot.cgi?channel=2")]
    [InlineData(MarkEnum.Axis, "/axis-cgi/jpg/image.cgi?camera=2")]
    public void SnapshotCandidates_PrioritizeTheManufacturerPath(MarkEnum mark, string expected)
    {
        var paths = CftvSnapshotService.CandidatePaths(mark, 2);

        Assert.Equal(expected, paths[0]);
        Assert.Contains("/snapshot.jpg", paths);
    }

    [Fact]
    public void CameraUpdateValidator_AcceptsCustomRtspPortAndOptionalPassword()
    {
        var input = new UpdateCftvDeviceIn
        {
            Name = "Câmera da entrada",
            IpAddress = "192.168.9.28",
            UserName = "admin",
            Password = null,
            HTTPPort = "80",
            RTSPPort = "39992",
            Mark = MarkEnum.Intelbras,
            DeviceType = CFTVDeviceTypeEnum.Camera,
            MaxChannels = 1
        };

        var result = new UpdateCftvDeviceInValidator().Validate(input);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("rtsp")]
    public void CameraUpdateValidator_RejectsInvalidRtspPort(string port)
    {
        var input = new UpdateCftvDeviceIn
        {
            Name = "Câmera",
            IpAddress = "192.168.0.20",
            UserName = "admin",
            HTTPPort = "80",
            RTSPPort = port,
            Mark = MarkEnum.Intelbras,
            DeviceType = CFTVDeviceTypeEnum.Camera,
            MaxChannels = 1
        };

        Assert.False(new UpdateCftvDeviceInValidator().Validate(input).IsValid);
    }

    [Fact]
    public void CameraUpdateValidator_RejectsDuplicateOrOutOfRangeChannels()
    {
        var input = new UpdateCftvDeviceIn
        {
            Name = "NVR Portaria",
            IpAddress = "192.168.0.20",
            UserName = "admin",
            HTTPPort = "80",
            RTSPPort = "554",
            Mark = MarkEnum.Intelbras,
            DeviceType = CFTVDeviceTypeEnum.NVR,
            MaxChannels = 2,
            Channels =
            [
                new() { ChannelNumber = 1, Name = "Entrada" },
                new() { ChannelNumber = 1, Name = "Garagem" },
                new() { ChannelNumber = 3, Name = "Elevador" }
            ]
        };

        Assert.False(new UpdateCftvDeviceInValidator().Validate(input).IsValid);
    }
}
