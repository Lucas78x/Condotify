using CondotifyAPI.Domain.Models.Equipments;

namespace CondotifyAPI.Data.Equipments
{
    public class CreateAccessControlDeviceByLicenseOut
    {
        public CreateAccessControlDeviceResult Result { get; set; }
        public AccessControlDevice Device { get; set; }
        public string Errors { get; set; }

    }
}
