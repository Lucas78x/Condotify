
using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Equipments
{
    public class CFTVDeviceDTO
    {
        public Guid Id { get; set; }

        public string Name { get; set; }
        public string Password { get; set; }
        public string IpAddress { get; set; }
        public string HTTPPort { get; set; }
        public string RTSPPort { get; set; }
        public IpTypeEnum IpType { get; set; }
        public ScreenProportionEnum Proportion { get; set; }
        public MarkEnum Mark { get; set; }

        //Reference Owner
        public LicenseDTO License { get; set; }
        public Guid LicenseId { get; set; }

    }
}
