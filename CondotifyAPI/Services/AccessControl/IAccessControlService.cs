using CondotifyAPI.Models.Equipments;

namespace CondotifyAPI.Services.AccessControl
{
    public interface IAccessControlService
    {
        Task<bool> TestConnectionAsync(AccessControlDevice device);
        Task<string> GetUsersAsync(AccessControlDevice device);
        Task<bool> AddUserAsync(AccessControlDevice device, object user);
        Task<bool> DeleteUserAsync(AccessControlDevice device, string userId);
        Task<string> GetEventsAsync(AccessControlDevice device);
    }
}
