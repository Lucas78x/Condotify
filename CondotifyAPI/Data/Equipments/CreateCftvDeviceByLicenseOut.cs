using CondotifyAPI.Domain.Models.Equipments;

namespace CondotifyAPI.Data.Equipments
{
    public class CreateCftvDeviceByLicenseOut
    {
        public CreateAccessControlDeviceResult Result { get; set; }
        public CftvDeviceResponse Device { get; set; }
        public string Errors { get; set; }
    }

}
