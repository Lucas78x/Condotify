using CondotifyAPI.Domain.DTO.Resident;

namespace CondotifyAPI.Domain.Enums.Resident
{
    public class ResidentAccessDeviceDTO
    {
        public Guid Id { get; set; }

        public Guid ResidentAccessCredentialId { get; set; }
        public ResidentAccessCredentialDTO Credential { get; set; }

        public Guid DeviceId { get; set; }
        public DeviceTypeEnum DeviceType { get; set; } 

        public string ExternalUserId { get; set; }         
        public string ExternalCredentialId { get; set; }   

        public string ExtraJson { get; set; }

        public bool IsSynced { get; set; }
        public DateTime LastSyncAt { get; set; }
    }

}
