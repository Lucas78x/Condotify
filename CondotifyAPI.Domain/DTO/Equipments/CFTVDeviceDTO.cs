
using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Equipments
{
    public class CFTVDeviceDTO
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        public string IpAddress { get; set; } = string.Empty;
        public string HTTPPort { get; set; } = "80";
        public string RTSPPort { get; set; } = "554";

        public IpTypeEnum IpType { get; set; }
        public ScreenProportionEnum Proportion { get; set; }
        public MarkEnum Mark { get; set; }

        public CFTVDeviceTypeEnum DeviceType { get; set; }

        public int MaxChannels { get; set; }

        public bool IsActive { get; set; }
        public bool ResidentVisible { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public string HealthMessage { get; set; } = string.Empty;

        public ICollection<CFTVChannelDTO> Channels { get; set; } = [];

        // Reference Owner
        public Guid LicenseId { get; set; }
        public LicenseDTO License { get; set; } = null!;
    }
}
