using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.AccessControl;

namespace CondotifyAPI.Domain.DTO.AccessControl;

public class AccessRouteDTO
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AccessRouteAudienceEnum Audience { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllowTemporary { get; set; }
    public int DaysOfWeekMask { get; set; } = 127;
    public TimeSpan StartTime { get; set; } = TimeSpan.Zero;
    public TimeSpan EndTime { get; set; } = new(23, 59, 59);
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<AccessRouteDeviceDTO> Devices { get; set; } = new List<AccessRouteDeviceDTO>();
    public ICollection<AccessRouteResidentOverrideDTO> ResidentOverrides { get; set; } = new List<AccessRouteResidentOverrideDTO>();
}

public class AccessRouteResidentOverrideDTO
{
    public Guid Id { get; set; }
    public Guid AccessRouteId { get; set; }
    public AccessRouteDTO AccessRoute { get; set; } = null!;
    public Guid ResidentId { get; set; }
    public ResidentAccessDTO Resident { get; set; } = null!;
    public AccessRouteOverrideModeEnum Mode { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AccessOperationAuditDTO
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AccessBatchOperationDTO
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string Operation { get; set; } = string.Empty;
    public AccessBatchStatusEnum Status { get; set; }
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int SuccessfulItems { get; set; }
    public int FailedItems { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public string FilterJson { get; set; } = "{}";
    public string Error { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

public class AccessEventRecordDTO
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid? CredentialId { get; set; }
    public string ExternalEventId { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
    public bool Authorized { get; set; }
    public DateTime OccurredAt { get; set; }
    public string ExternalUserId { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public string Credential { get; set; } = string.Empty;
    public string Portal { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public LicenseDTO License { get; set; } = null!;
    public AccessControlDeviceDTO Device { get; set; } = null!;
    public ResidentAccessCredentialDTO? AccessCredential { get; set; }
}

public class AccessRouteDeviceDTO
{
    public Guid Id { get; set; }
    public Guid AccessRouteId { get; set; }
    public AccessRouteDTO AccessRoute { get; set; } = null!;
    public Guid DeviceId { get; set; }
    public AccessControlDeviceDTO Device { get; set; } = null!;
    public int PortalNumber { get; set; } = 1;
    public AccessRouteDirectionEnum Direction { get; set; } = AccessRouteDirectionEnum.Entry;
    public bool IsActive { get; set; } = true;
}
