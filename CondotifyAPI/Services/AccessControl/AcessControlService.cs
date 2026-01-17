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

        public Task<string> GetUsersAsync(AccessControlDevice device)
        {
            var driver = _driverFactory.GetDriver(device.Type);
            return driver.GetEventsAsync(device);
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

        public Task<bool> OpenDoorAsync(AccessControlDevice device)
        {
            var driver = _driverFactory.GetDriver(device.Type);
            return driver.OpenDoorAsync(device);
        }

        public Task<bool> TestConnectionAsync(AccessControlDevice device)
        {
            var driver = _driverFactory.GetDriver(device.Type);
            return driver.TestConnectionAsync(device);
        }
    }
}
