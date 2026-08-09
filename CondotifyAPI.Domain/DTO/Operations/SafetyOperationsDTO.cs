using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Operations;

public enum IncidentSeverityEnum { Low = 0, Medium = 1, High = 2, Critical = 3 }
public enum IncidentStatusEnum { Open = 0, InProgress = 1, Resolved = 2, Closed = 3 }
public enum IncidentSourceEnum { Manual = 0, Automation = 1, Emergency = 2, System = 3 }
public enum IncidentCategoryEnum { Access = 0, Visitor = 1, Vehicle = 2, Device = 3, Delivery = 4, Safety = 5, Other = 6 }
public enum IncidentTimelineTypeEnum { Created = 0, Comment = 1, StatusChanged = 2, Assignment = 3, Evidence = 4, Automation = 5, Emergency = 6 }

public sealed class IncidentDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IncidentCategoryEnum Category { get; set; }
    public IncidentSeverityEnum Severity { get; set; }
    public IncidentStatusEnum Status { get; set; }
    public IncidentSourceEnum Source { get; set; }
    public string RelatedResourceType { get; set; } = string.Empty;
    public Guid? RelatedResourceId { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public string AssignedToName { get; set; } = string.Empty;
    public Guid? ReportedByUserId { get; set; }
    public string ReportedByName { get; set; } = string.Empty;
    public DateTime? DueAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string Resolution { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<IncidentTimelineEntryDTO> Timeline { get; set; } = new List<IncidentTimelineEntryDTO>();
}

public sealed class IncidentTimelineEntryDTO
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public IncidentDTO Incident { get; set; } = null!;
    public IncidentTimelineTypeEnum Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ReferenceType { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
    public string ReferenceUrl { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
    public Guid? ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public enum AutomationTriggerTypeEnum { DeviceOffline = 0, AccessDeniedThreshold = 1, VisitorOverstay = 2, DeliveryOverdue = 3 }

[Flags]
public enum AutomationActionEnum { None = 0, CreateIncident = 1, CreateAlert = 2 }

public sealed class AutomationRuleDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AutomationTriggerTypeEnum TriggerType { get; set; }
    public int Threshold { get; set; } = 5;
    public int WindowMinutes { get; set; } = 15;
    public IncidentSeverityEnum Severity { get; set; } = IncidentSeverityEnum.High;
    public AutomationActionEnum Actions { get; set; } = AutomationActionEnum.CreateIncident | AutomationActionEnum.CreateAlert;
    public bool IsEnabled { get; set; } = true;
    public int CooldownMinutes { get; set; } = 60;
    public DateTime? LastEvaluatedAt { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<AutomationExecutionDTO> Executions { get; set; } = new List<AutomationExecutionDTO>();
}

public sealed class AutomationExecutionDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid RuleId { get; set; }
    public AutomationRuleDTO Rule { get; set; } = null!;
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string Fingerprint { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public Guid? IncidentId { get; set; }
    public IncidentDTO? Incident { get; set; }
    public Guid? AlertId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum EmergencyTypeEnum { Lockdown = 0, Evacuation = 1, Panic = 2, Fire = 3, Medical = 4, Other = 5 }
public enum EmergencyStatusEnum { Active = 0, Resolved = 1, Cancelled = 2 }

public sealed class EmergencySessionDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public EmergencyTypeEnum Type { get; set; }
    public EmergencyStatusEnum Status { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public Guid? ActivatedByUserId { get; set; }
    public string ActivatedByName { get; set; } = string.Empty;
    public DateTime ActivatedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string ResolvedByName { get; set; } = string.Empty;
    public DateTime? ResolvedAt { get; set; }
    public string Resolution { get; set; } = string.Empty;
    public Guid IncidentId { get; set; }
    public IncidentDTO Incident { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum DigitalPassStatusEnum { Active = 0, Revoked = 1, Expired = 2 }

public sealed class DigitalPassDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public Guid VisitId { get; set; }
    public AccessVisitDTO Visit { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public DigitalPassStatusEnum Status { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastViewedAt { get; set; }
    public int ViewCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
