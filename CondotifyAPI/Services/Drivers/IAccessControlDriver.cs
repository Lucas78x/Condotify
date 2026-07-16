using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Services.AccessControl;

namespace CondotifyAPI.Services.Drivers
{
    public interface IAccessControlDriver
    {
        bool Supports(DeviceTypeEnum deviceType);

        Task<bool> TestConnectionAsync(CreateAccessControlDeviceByLicenseIn device);
        Task<bool> OpenDoorAsync(AccessControlDevice device, int channel);
        Task<CredentialOperationResult> UpsertCredentialAsync(AccessControlDevice device, CredentialProvisionRequest request);
        Task<CredentialOperationResult> SetCredentialActiveAsync(AccessControlDevice device, CredentialProvisionRequest request, bool isActive);
        Task<CredentialOperationResult> RemoveCredentialAsync(AccessControlDevice device, CredentialProvisionRequest request);
        Task<CredentialOperationResult> StartFaceEnrollmentAsync(AccessControlDevice device, string externalUserId);
        Task<CredentialOperationResult> CancelFaceEnrollmentAsync(AccessControlDevice device);
        Task<IReadOnlyList<DeviceAccessEvent>> GetAccessEventsAsync(AccessControlDevice device, int take);
        Task<DeviceInspectionResult> InspectAsync(AccessControlDevice device) =>
            Task.FromResult(DeviceInspectionResult.Unavailable("Este driver ainda nao fornece diagnostico detalhado."));

        Task<string> GetUsersAsync(AccessControlDevice device);

        Task<bool> AddUserAsync(AccessControlDevice device, object user);
        Task<bool> DeleteUserAsync(AccessControlDevice device, string userId);
        Task<string> GetEventsAsync(AccessControlDevice device);
    }


}
