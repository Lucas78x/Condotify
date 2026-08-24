using CondotifyAPI.Commands.Equipments;
using CondotifyAPI.Domain.Models.Equipments;

namespace CondotifyAPI.Data.Equipments
{
    public class CreateCftvDeviceByLicenseIn
    {
        public string LicenseId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = "";
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";

        public string HTTPPort { get; set; } = "80";
        public string RTSPPort { get; set; } = "554";

        public IpTypeEnum IpType { get; set; }
        public ScreenProportionEnum Proportion { get; set; }
        public MarkEnum Mark { get; set; }

        public CFTVDeviceTypeEnum DeviceType { get; set; }

        public int MaxChannels { get; set; }
        public bool ResidentVisible { get; set; }

        public ICollection<CFTVChannel> Channels { get; set; } = [];
    }

    public static class CreateCftvDeviceByLicenseInConverter
    {
        public static CreateCftvDeviceByLicenseCommand ToCommand(this CreateCftvDeviceByLicenseIn device)
        {
            device.Channels ??= [];
            if (device.DeviceType == CFTVDeviceTypeEnum.Camera && device.Channels.Count == 0)
            {
                device.Channels.Add(new CFTVChannel
                {
                    Id = Guid.NewGuid(),
                    ChannelNumber = 1,
                    Name = string.IsNullOrWhiteSpace(device.Name) ? "Câmera" : device.Name.Trim(),
                    IsEnabled = true,
                    ResidentVisible = device.ResidentVisible
                });
            }
            // Compatibilidade com versões anteriores do portal, que enviavam a
            // visibilidade apenas no equipamento e não em cada canal.
            if (device.ResidentVisible && device.Channels.All(channel => !channel.ResidentVisible))
            {
                foreach (var channel in device.Channels.Where(channel => channel.IsEnabled))
                    channel.ResidentVisible = true;
            }

            return new CreateCftvDeviceByLicenseCommand(
                Guid.Parse(device.LicenseId),
                device.Name,
                device.IpAddress,
                device.UserName, 
                device.Password,
                string.IsNullOrWhiteSpace(device.HTTPPort) ? "80" : device.HTTPPort,
                string.IsNullOrWhiteSpace(device.RTSPPort) ? "554" : device.RTSPPort,
                device.IpType,
                device.Proportion,
                device.Mark,
                device.DeviceType,
                device.MaxChannels,
                device.Channels ?? [],
                device.ResidentVisible
            );
        }
    }
}
