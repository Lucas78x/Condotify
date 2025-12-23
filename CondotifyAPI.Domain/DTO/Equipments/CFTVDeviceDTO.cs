
using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Equipments
{
    public class CFTVDeviceDTO
    {
        public Guid Id { get; set; }

        public string Name { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }

        public string IpAddress { get; set; }
        public string HTTPPort { get; set; }
        public string RTSPPort { get; set; }

        public IpTypeEnum IpType { get; set; }
        public ScreenProportionEnum Proportion { get; set; }
        public MarkEnum Mark { get; set; }

        public CFTVDeviceTypeEnum DeviceType { get; set; }

        public int MaxChannels { get; set; }

        public ICollection<CFTVChannelDTO> Channels { get; set; }

        // Reference Owner
        public Guid LicenseId { get; set; }
        public LicenseDTO License { get; set; }
    }
}
