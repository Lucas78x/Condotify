using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Services.Factorys;

namespace CondotifyAPI.Services.AccessControl
{
    public class AccessControlService : IAccessControlService
    {
        private readonly IAccessControlDriverFactory _driverFactory;

        public AccessControlService(IAccessControlDriverFactory driverFactory)
        {
            _driverFactory = driverFactory;
        }

        public Task<bool> TestConnectionAsync(CreateAccessControlDeviceByLicenseIn device)
        {
            var driver = _driverFactory.GetDriver(device.Type);
            return driver.TestConnectionAsync(device);
        }

        public Task<bool> OpenDoorAsync(AccessControlDevice device, int channel)
        {
            var driver = _driverFactory.GetDriver(device.Type);
            return driver.OpenDoorAsync(device, channel);
        }

        public Task<CredentialOperationResult> UpsertCredentialAsync(AccessControlDevice device, CredentialProvisionRequest request) =>
            _driverFactory.GetDriver(device.Type).UpsertCredentialAsync(device, request);

        public Task<CredentialOperationResult> SetCredentialActiveAsync(AccessControlDevice device, CredentialProvisionRequest request, bool isActive) =>
            _driverFactory.GetDriver(device.Type).SetCredentialActiveAsync(device, request, isActive);

        public Task<CredentialOperationResult> RemoveCredentialAsync(AccessControlDevice device, CredentialProvisionRequest request) =>
            _driverFactory.GetDriver(device.Type).RemoveCredentialAsync(device, request);

        public Task<CredentialOperationResult> StartFaceEnrollmentAsync(AccessControlDevice device, string externalUserId) =>
            _driverFactory.GetDriver(device.Type).StartFaceEnrollmentAsync(device, externalUserId);

        public Task<CredentialOperationResult> CancelFaceEnrollmentAsync(AccessControlDevice device) =>
            _driverFactory.GetDriver(device.Type).CancelFaceEnrollmentAsync(device);

        public Task<IReadOnlyList<DeviceAccessEvent>> GetAccessEventsAsync(AccessControlDevice device, int take) =>
            _driverFactory.GetDriver(device.Type).GetAccessEventsAsync(device, take);

        public Task<DeviceInspectionResult> InspectAsync(AccessControlDevice device) =>
            _driverFactory.GetDriver(device.Type).InspectAsync(device);

        public Task<DeviceCredentialInventoryResult> ReadCredentialInventoryAsync(AccessControlDevice device) =>
            _driverFactory.GetDriver(device.Type).ReadCredentialInventoryAsync(device);

        public Task<string> GetUsersAsync(AccessControlDevice device)
        {
            var driver = _driverFactory.GetDriver(device.Type);
            return driver.GetUsersAsync(device);
        }

        public Task<bool> AddUserAsync(AccessControlDevice device, object user)
        {
            var driver = _driverFactory.GetDriver(device.Type);
            return driver.AddUserAsync(device, user);
        }

        public Task<bool> DeleteUserAsync(AccessControlDevice device, string userId)
        {
            var driver = _driverFactory.GetDriver(device.Type);
            return driver.DeleteUserAsync(device, userId);
        }

        public Task<string> GetEventsAsync(AccessControlDevice device)
        {
            var driver = _driverFactory.GetDriver(device.Type);
            return driver.GetEventsAsync(device);
        }
    }
}
