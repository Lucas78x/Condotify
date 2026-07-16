using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models.Equipments;

namespace CondotifyAPI.Services.AccessControl
{
    public interface IAccessControlService
    {
        Task<bool> TestConnectionAsync(CreateAccessControlDeviceByLicenseIn device);
        Task<bool> OpenDoorAsync(AccessControlDevice device, int channel);
        Task<CredentialOperationResult> UpsertCredentialAsync(AccessControlDevice device, CredentialProvisionRequest request);
        Task<CredentialOperationResult> SetCredentialActiveAsync(AccessControlDevice device, CredentialProvisionRequest request, bool isActive);
        Task<CredentialOperationResult> RemoveCredentialAsync(AccessControlDevice device, CredentialProvisionRequest request);
        Task<CredentialOperationResult> StartFaceEnrollmentAsync(AccessControlDevice device, string externalUserId);
        Task<CredentialOperationResult> CancelFaceEnrollmentAsync(AccessControlDevice device);
        Task<IReadOnlyList<DeviceAccessEvent>> GetAccessEventsAsync(AccessControlDevice device, int take);
        Task<DeviceInspectionResult> InspectAsync(AccessControlDevice device);

        Task<string> GetUsersAsync(AccessControlDevice device);
        Task<bool> AddUserAsync(AccessControlDevice device, object user);
        Task<bool> DeleteUserAsync(AccessControlDevice device, string userId);
        Task<string> GetEventsAsync(AccessControlDevice device);
    }
}
