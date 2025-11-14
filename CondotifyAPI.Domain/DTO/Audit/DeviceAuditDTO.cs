using CondotifyAPI.Domain.DTO.Equipments;

namespace CondotifyAPI.Domain.DTO.Audit
{
    public class DeviceAuditDTO
    {
        public Guid Id { get; set; } 
        public ActionTypeEnum Action { get; set; } 
        public string ChangedFields { get; set; } 
        public DateTime Timestamp { get; set; }
        public Guid UserId { get; set; }
        public string UserName { get; set; }

        public Guid DeviceId { get; set; }
        public AccessControlDeviceDTO Device { get; set; }
    }
}
