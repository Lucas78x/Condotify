using CondotifyAPI.Commands.Equipments;
using CondotifyAPI.Domain.Models;

namespace CondotifyAPI.Data.Equipments
{
    public class CreateAccessControlDeviceByLicenseIn
    {
        public string LicenseId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
        public string IPAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? MACAddress { get; set; }
        public string Model { get; set; } = string.Empty;
        public string? SerialNumber { get; set; }
        public string? FirmwareVersion { get; set; }
        public DeviceTypeEnum Type { get; set; }
        public bool IsActive { get; set; }
        public Location Location { get; set; } = new();
    }

    public static class CreateAccessControlDeviceByLicenseInConverter
    {
        public static CreateAccessControlDeviceByLicenseCommand ToCommand(this CreateAccessControlDeviceByLicenseIn device)
        {
            if (string.IsNullOrWhiteSpace(device.Location.Name))
                device.Location.Name = device.Name;

            return new CreateAccessControlDeviceByLicenseCommand(
                Guid.Parse(device.LicenseId),
                device.Name,
                device.IPAddress,
                device.Port,
                device.Username,
                device.Password,
                device.MACAddress,
                device.Model,
                device.SerialNumber,
                device.FirmwareVersion,
                device.Type,
                device.IsActive,
                device.Location
            );
        }
    }
}
