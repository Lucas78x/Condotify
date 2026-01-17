using CondotifyAPI.Domain.Models.Enterprises;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Domain.Models.License;
using CondotifyAPI.Domain.Models.Users;

namespace CondotifyAPI.Domain.Interfaces;

public interface ICondotifyQueriesRepository
{
    // USERS
    Task<UserAccess?> GetUserByIdAsync(Guid userId);
    Task<UserAccess?> GetUserByEmailAsync(string email);

    // ENTERPRISE
    Task<Enterprise?> GetEnterpriseByIdAsync(Guid enterpriseId);
    Task<List<Enterprise>> GetEnterprisesAsync();

    // LICENSE
    Task<License?> GetLicenseByIdAsync(Guid licenseId);
    Task<List<License>> GetLicensesByEnterpriseAsync(Guid enterpriseId);

    // ACCESS CONTROL DEVICES
    Task<AccessControlDevice?> GetAccessControlDeviceByIdAsync(Guid deviceId);
    Task<List<AccessControlDevice>> GetAccessControlDevicesByLicenseAsync(Guid licenseId);

    // CFTV DEVICES
    Task<CFTVDevice?> GetCFTVDeviceByIdAsync(Guid deviceId);
    Task<List<CFTVDevice>> GetCFTVDevicesByLicenseAsync(Guid licenseId);
    Task <AccessControlDevice> GetDeviceByDeviceIdAsync(Guid deviceId);
}
