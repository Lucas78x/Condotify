using CondotifyAPI.Domain.Models.Equipments;

namespace CondotifyAPI.Data.Equipments
{
    public class CreateAccessControlDeviceByLicenseOut
    {
        public CreateAccessControlDeviceResult Result { get; set; }
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool ConnectionTested { get; set; }
        public bool ConnectionSucceeded { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Errors { get; set; } = string.Empty;

    }
}
