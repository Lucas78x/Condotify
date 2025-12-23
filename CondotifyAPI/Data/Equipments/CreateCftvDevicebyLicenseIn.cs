using CondotifyAPI.Commands.Equipments;
using CondotifyAPI.Domain.Models.Equipments;

namespace CondotifyAPI.Data.Equipments
{
    public class CreateCftvDeviceByLicenseIn
    {
        public string LicenseId { get; set; }

        public string Name { get; set; }
        public string IpAddress { get; set; } = "";
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";

        public string? HTTPPort { get; set; }
        public string? RTSPPort { get; set; }

        public IpTypeEnum IpType { get; set; }
        public ScreenProportionEnum Proportion { get; set; }
        public MarkEnum Mark { get; set; }

        public CFTVDeviceTypeEnum DeviceType { get; set; }

        public int MaxChannels { get; set; }

        public ICollection<CFTVChannel> Channels { get; set; }
    }

    public static class CreateCftvDeviceByLicenseInConverter
    {
        public static CreateCftvDeviceByLicenseCommand ToCommand(this CreateCftvDeviceByLicenseIn device)
        {
            return new CreateCftvDeviceByLicenseCommand(
                Guid.Parse(device.LicenseId),
                device.Name,
                device.IpAddress,
                device.UserName, 
                device.Password,
                device.HTTPPort,
                device.RTSPPort,
                device.IpType,
                device.Proportion,
                device.Mark,
                device.DeviceType,
                device.MaxChannels,
                device.Channels
            );
        }
    }
}
