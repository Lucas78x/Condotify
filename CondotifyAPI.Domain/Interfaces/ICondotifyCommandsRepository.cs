using CondotifyAPI.Domain.Models.Enterprises;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Domain.Models.License;
using CondotifyAPI.Domain.Models.Users;

namespace CondotifyAPI.Domain.Interfaces;

public interface ICondotifyCommandsRepository
{
    Task<AccessControlDevice> AddAccessControlDeviceAsync(Guid licenseId, AccessControlDevice device);
    Task<CFTVDevice> AddCftvDeviceAsync(Guid licenseId, CFTVDevice device);
    Task<EnterpriseCreateResult> AddEnterpriseAsync(Enterprise enterprise);
    Task<License> AddLicenseAsync(Guid enterpriseId, License license);
    Task<UserAccessCreateResult> AddUserAccessAsync(UserAccess  user);
}
