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
            HTTPPort = null!,
            RTSPPort = null!
        };

        var command = input.ToCommand();

        Assert.Equal("80", command.HTTPPort);
        Assert.Equal("554", command.RTSPPort);
        Assert.NotNull(command.Channels);
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
}
