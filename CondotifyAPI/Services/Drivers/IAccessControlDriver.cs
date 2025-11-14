using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models.Equipments;

namespace CondotifyAPI.Services.Drivers
{
    public interface IAccessControlDriver
    {
        bool Supports(DeviceTypeEnum deviceType);

        Task<bool> TestConnectionAsync(CreateAccessControlDeviceByLicenseIn device);
        Task<string> GetUsersAsync(AccessControlDevice device);
        Task<bool> AddUserAsync(AccessControlDevice device, object user);
        Task<bool> DeleteUserAsync(AccessControlDevice device, string userId);
        Task<string> GetEventsAsync(AccessControlDevice device);
    }


}
