using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.Drivers;
using CondotifyAPI.Services.Factorys;

namespace CondotifyAPI.Tests;

public class AccessControlServiceTests
{
    [Fact]
    public void DeviceRegistration_ShouldBeStableAndPositive()
    {
        var residentId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        var first = AccessControlDeviceRegistration.FromResidentId(residentId);
        var second = AccessControlDeviceRegistration.FromResidentId(residentId);

        Assert.Equal(first, second);
        Assert.True(long.Parse(first) > 0);
    }

    [Fact]
    public async Task OpenDoorAsync_ShouldUseDriverThatSupportsDeviceType()
    {
        var driver = new RecordingDriver(DeviceTypeEnum.IdFace);
        var service = new AccessControlService(new AccessControlDriverFactory([driver]));
        var device = new AccessControlDevice
        {
            Id = Guid.NewGuid(),
            Name = "Portaria",
            Type = DeviceTypeEnum.IdFace
        };

        var result = await service.OpenDoorAsync(device, 2);

        Assert.True(result);
        Assert.Same(device, driver.LastDevice);
        Assert.Equal(2, driver.LastChannel);
    }

    [Fact]
    public async Task OpenDoorAsync_ShouldRejectUnsupportedDeviceType()
    {
        var service = new AccessControlService(
            new AccessControlDriverFactory([new RecordingDriver(DeviceTypeEnum.IdFace)]));
        var device = new AccessControlDevice { Type = DeviceTypeEnum.SS5520 };

        var error = await Assert.ThrowsAsync<NotSupportedException>(
            () => service.OpenDoorAsync(device, 1));

        Assert.Contains(nameof(DeviceTypeEnum.SS5520), error.Message);
    }

    private sealed class RecordingDriver(DeviceTypeEnum supportedType) : IAccessControlDriver
    {
        public AccessControlDevice? LastDevice { get; private set; }
        public int? LastChannel { get; private set; }

        public bool Supports(DeviceTypeEnum deviceType) => deviceType == supportedType;

        public Task<bool> OpenDoorAsync(AccessControlDevice device, int channel)
        {
            LastDevice = device;
            LastChannel = channel;
            return Task.FromResult(true);
        }

        public Task<CredentialOperationResult> UpsertCredentialAsync(AccessControlDevice device, CredentialProvisionRequest request) =>
            Task.FromResult(CredentialOperationResult.Ok("1", "1"));

        public Task<CredentialOperationResult> SetCredentialActiveAsync(AccessControlDevice device, CredentialProvisionRequest request, bool isActive) =>
            Task.FromResult(CredentialOperationResult.Ok("1", "1"));

        public Task<CredentialOperationResult> RemoveCredentialAsync(AccessControlDevice device, CredentialProvisionRequest request) =>
            Task.FromResult(CredentialOperationResult.Ok("1", "1"));

        public Task<CredentialOperationResult> StartFaceEnrollmentAsync(AccessControlDevice device, string externalUserId) =>
            Task.FromResult(CredentialOperationResult.Ok(externalUserId, "face"));

        public Task<CredentialOperationResult> CancelFaceEnrollmentAsync(AccessControlDevice device) =>
            Task.FromResult(CredentialOperationResult.Ok(null, null));

        public Task<IReadOnlyList<DeviceAccessEvent>> GetAccessEventsAsync(AccessControlDevice device, int take) =>
            Task.FromResult<IReadOnlyList<DeviceAccessEvent>>([]);

        public Task<bool> TestConnectionAsync(CreateAccessControlDeviceByLicenseIn device) => Task.FromResult(true);
        public Task<string> GetUsersAsync(AccessControlDevice device) => Task.FromResult("[]");
        public Task<bool> AddUserAsync(AccessControlDevice device, object user) => Task.FromResult(true);
        public Task<bool> DeleteUserAsync(AccessControlDevice device, string userId) => Task.FromResult(true);
        public Task<string> GetEventsAsync(AccessControlDevice device) => Task.FromResult("[]");
    }
}
