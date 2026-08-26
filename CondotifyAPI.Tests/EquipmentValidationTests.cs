using CondotifyAPI.Commands.Equipments;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models;

namespace CondotifyAPI.Tests;

public sealed class EquipmentValidationTests
{
    [Fact]
    public void CftvInput_ShouldUseSafeDefaultPorts()
    {
        var input = new CreateCftvDeviceByLicenseIn
        {
            LicenseId = Guid.NewGuid().ToString(),
            Name = "Entrada",
            DeviceType = CFTVDeviceTypeEnum.Camera,
            ResidentVisible = true,
            HTTPPort = null!,
            RTSPPort = null!
        };

        var command = input.ToCommand();

        Assert.Equal("80", command.HTTPPort);
        Assert.Equal("554", command.RTSPPort);
        var channel = Assert.Single(command.Channels);
        Assert.Equal(1, channel.ChannelNumber);
        Assert.True(channel.ResidentVisible);
    }

    [Theory]
    [InlineData("not-an-ip", 80)]
    [InlineData("192.168.1.10", 0)]
    [InlineData("192.168.1.10", 65536)]
    public void AccessControlValidator_ShouldRejectInvalidNetworkEndpoint(string ipAddress, int port)
    {
        var command = new CreateAccessControlDeviceByLicenseCommand(
            Guid.NewGuid(),
            "Portaria",
            ipAddress,
            port,
            "admin",
            "secret",
            null,
            "Modelo",
            null,
            null,
            DeviceTypeEnum.IdFace,
            true,
            new Location());

        var result = new CreateAccessControlDeviceByLicenseCommandValidator().Validate(command);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("169.254.169.254")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    public void EquipmentValidators_ShouldRejectSensitiveDestinations(string ipAddress)
    {
        var access = new CreateAccessControlDeviceByLicenseCommand(
            Guid.NewGuid(), "Portaria", ipAddress, 80, "admin", "secret", null,
            "Modelo", null, null, DeviceTypeEnum.IdFace, true, new Location());
        var cftv = new CreateCftvDeviceByLicenseCommand(
            Guid.NewGuid(), "Camera", ipAddress, "admin", "secret", "80", "554",
            IpTypeEnum.Ipv4, ScreenProportionEnum.Widescreen, MarkEnum.Intelbras,
            CFTVDeviceTypeEnum.Camera, 1, []);

        Assert.False(new CreateAccessControlDeviceByLicenseCommandValidator().Validate(access).IsValid);
        Assert.False(new CreateCftvDeviceByLicenseCommandValidator().Validate(cftv).IsValid);
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("10.0.0.20")]
    [InlineData("172.16.1.30")]
    [InlineData("2001:db8::10")]
    public void EquipmentValidators_ShouldAllowUnicastDeviceAddresses(string ipAddress)
    {
        var command = new CreateAccessControlDeviceByLicenseCommand(
            Guid.NewGuid(), "Portaria", ipAddress, 80, "admin", "secret", null,
            "Modelo", null, null, DeviceTypeEnum.IdFace, true, new Location());

        Assert.True(new CreateAccessControlDeviceByLicenseCommandValidator().Validate(command).IsValid);
    }
}
