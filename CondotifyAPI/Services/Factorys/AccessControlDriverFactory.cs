using CondotifyAPI.Services.Drivers;

namespace CondotifyAPI.Services.Factorys
{
    public class AccessControlDriverFactory : IAccessControlDriverFactory
    {
        private readonly IEnumerable<IAccessControlDriver> _drivers;

        public AccessControlDriverFactory(IEnumerable<IAccessControlDriver> drivers)
        {
            _drivers = drivers;
        }

        public IAccessControlDriver GetDriver(DeviceTypeEnum type)
        {
            var driver = _drivers.FirstOrDefault(d => d.Supports(type));

            if (driver == null)
                throw new NotSupportedException($"No driver registered for device type: {type}");

            return driver;
        }
    }

}
