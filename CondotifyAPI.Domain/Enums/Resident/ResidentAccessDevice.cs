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
        public CondotifyAPI.Domain.DTO.Equipments.AccessControlDeviceDTO Device { get; set; }

        public string ExternalUserId { get; set; }         
        public string ExternalCredentialId { get; set; }   

        public string ExtraJson { get; set; }

        public bool IsSynced { get; set; }
        public DateTime LastSyncAt { get; set; }
        public CondotifyAPI.Domain.Enums.AccessControl.CredentialSyncStatusEnum SyncStatus { get; set; } = CondotifyAPI.Domain.Enums.AccessControl.CredentialSyncStatusEnum.Pending;
        public int AttemptCount { get; set; }
        public DateTime? NextAttemptAt { get; set; }
        public DateTime? LastSuccessAt { get; set; }
        public DateTime? LastErrorAt { get; set; }
        public string RouteNames { get; set; } = string.Empty;
        public string PortalNumbers { get; set; } = string.Empty;
    }

}
