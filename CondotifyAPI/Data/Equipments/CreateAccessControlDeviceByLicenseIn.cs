using CondotifyAPI.Commands.Equipments;
using CondotifyAPI.Domain.Models;
using DigitalWorldOnline.Management.Api.Data;
using System.Text;

namespace CondotifyAPI.Data.Equipments
{
    public class CreateAccessControlDeviceByLicenseIn
    {
        public string LicenseId { get; set; }

        public string Name { get; set; }
        public string IPAddress { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string MACAddress { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string FirmwareVersion { get; set; }
        public DeviceTypeEnum Type { get; set; }
        public bool IsActive { get; set; }
        public Location Location { get; set; }
    }

    public static class CreateAccessControlDeviceByLicenseInConverter
    {
        public static CreateAccessControlDeviceByLicenseCommand ToCommand(this CreateAccessControlDeviceByLicenseIn device)
        {
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
