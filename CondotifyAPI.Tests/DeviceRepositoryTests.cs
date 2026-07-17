using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Infrastructure.Repositories;

namespace CondotifyAPI.Tests;

public sealed class DeviceRepositoryTests
{
    [Fact]
    public void ConflictPredicate_ShouldIgnoreMissingOptionalIdentifiers()
    {
        var licenseId = Guid.NewGuid();
        var predicate = CondotifyCommandsRepository.BuildAccessControlDeviceConflictPredicate(
            licenseId, null, null, "192.168.0.20", 80).Compile();

        var existing = Device(licenseId, "192.168.0.10", 80, null, null);

        Assert.False(predicate(existing));
    }

    [Fact]
    public void ConflictPredicate_ShouldMatchIpSerialOrMacWithinLicense()
    {
        var licenseId = Guid.NewGuid();
        var otherLicenseId = Guid.NewGuid();
        var predicate = CondotifyCommandsRepository.BuildAccessControlDeviceConflictPredicate(
            licenseId, "SERIAL-01", "AA:BB:CC:DD:EE:FF", "192.168.0.20", 80).Compile();

        Assert.True(predicate(Device(licenseId, "192.168.0.20", 80, null, null)));
        Assert.True(predicate(Device(licenseId, "192.168.0.30", 80, "SERIAL-01", null)));
        Assert.True(predicate(Device(licenseId, "192.168.0.30", 80, null, "AA:BB:CC:DD:EE:FF")));
        Assert.False(predicate(Device(otherLicenseId, "192.168.0.20", 80, "SERIAL-01", "AA:BB:CC:DD:EE:FF")));
    }

    private static AccessControlDeviceDTO Device(
        Guid licenseId,
        string ipAddress,
        int port,
        string? serialNumber,
        string? macAddress) => new()
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Name = "Terminal",
            IPAddress = ipAddress,
            Port = port,
            Username = "admin",
            Password = "secret",
            Model = "IdFace",
            SerialNumber = serialNumber,
            MACAddress = macAddress
        };
}
