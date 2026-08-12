using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Operations;

public enum IncidentSeverityEnum { Low = 0, Medium = 1, High = 2, Critical = 3 }
public enum IncidentStatusEnum { Open = 0, InProgress = 1, Resolved = 2, Closed = 3 }
public enum IncidentSourceEnum { Manual = 0, Automation = 1, Emergency = 2, System = 3 }
public enum IncidentCategoryEnum
{
    Access = 0, Visitor = 1, Vehicle = 2, Device = 3, Delivery = 4, Safety = 5, Other = 6,
    Hydraulic = 7, Electrical = 8, Elevator = 9, Cleaning = 10, Structural = 11, Landscaping = 12
}
public enum IncidentTimelineTypeEnum { Created = 0, Comment = 1, StatusChanged = 2, Assignment = 3, Evidence = 4, Automation = 5, Emergency = 6 }
public enum WorkOrderStatusEnum { Planned = 0, Assigned = 1, InProgress = 2, WaitingProvider = 3, WaitingMaterial = 4, Completed = 5, Cancelled = 6 }
public enum WorkOrderActivityTypeEnum { Created = 0, StatusChanged = 1, Assignment = 2, Checklist = 3, Cost = 4, Comment = 5, Completed = 6 }

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
    public Guid? ReportedByResidentId { get; set; }
    public string ReportedByName { get; set; } = string.Empty;
    public string LocationLabel { get; set; } = string.Empty;
    public DateTime? DueAt { get; set; }
    public DateTime? SlaResponseDueAt { get; set; }
    public DateTime? SlaResolutionDueAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string Resolution { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<IncidentTimelineEntryDTO> Timeline { get; set; } = new List<IncidentTimelineEntryDTO>();
    public ICollection<IncidentAttachmentDTO> Attachments { get; set; } = new List<IncidentAttachmentDTO>();
    public ICollection<WorkOrderDTO> WorkOrders { get; set; } = new List<WorkOrderDTO>();
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
    public bool VisibleToResident { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class MaintenancePolicyDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public int LowResponseMinutes { get; set; } = 1440;
    public int LowResolutionMinutes { get; set; } = 10080;
    public int MediumResponseMinutes { get; set; } = 480;
    public int MediumResolutionMinutes { get; set; } = 4320;
    public int HighResponseMinutes { get; set; } = 120;
    public int HighResolutionMinutes { get; set; } = 1440;
    public int CriticalResponseMinutes { get; set; } = 30;
    public int CriticalResolutionMinutes { get; set; } = 240;
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

public sealed class MaintenanceProviderDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<WorkOrderDTO> WorkOrders { get; set; } = new List<WorkOrderDTO>();
}

public sealed class PreventiveMaintenancePlanDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LocationLabel { get; set; } = string.Empty;
    public Guid? DeviceId { get; set; }
    public int IntervalDays { get; set; } = 30;
    public int LeadDays { get; set; } = 3;
    public DateTime NextDueAt { get; set; }
    public DateTime? LastGeneratedFor { get; set; }
    public Guid? DefaultProviderId { get; set; }
    public MaintenanceProviderDTO? DefaultProvider { get; set; }
    public string DefaultAssignedToName { get; set; } = string.Empty;
    public decimal EstimatedCost { get; set; }
    public string ChecklistTemplateJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<WorkOrderDTO> WorkOrders { get; set; } = new List<WorkOrderDTO>();
}

public sealed class WorkOrderDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public Guid? IncidentId { get; set; }
    public IncidentDTO? Incident { get; set; }
    public Guid? PreventivePlanId { get; set; }
    public PreventiveMaintenancePlanDTO? PreventivePlan { get; set; }
    public DateTime? ScheduledFor { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public WorkOrderStatusEnum Status { get; set; }
    public IncidentSeverityEnum Priority { get; set; }
    public string LocationLabel { get; set; } = string.Empty;
    public Guid? AssignedToUserId { get; set; }
    public string AssignedToName { get; set; } = string.Empty;
    public Guid? ProviderId { get; set; }
    public MaintenanceProviderDTO? Provider { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal ActualCost { get; set; }
    public string CompletionNotes { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<WorkOrderChecklistItemDTO> Checklist { get; set; } = new List<WorkOrderChecklistItemDTO>();
    public ICollection<WorkOrderActivityDTO> Activities { get; set; } = new List<WorkOrderActivityDTO>();
    public ICollection<IncidentAttachmentDTO> Attachments { get; set; } = new List<IncidentAttachmentDTO>();
}

public sealed class WorkOrderChecklistItemDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid WorkOrderId { get; set; }
    public WorkOrderDTO WorkOrder { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
    public bool IsCompleted { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public string CompletedByName { get; set; } = string.Empty;
    public DateTime? CompletedAt { get; set; }
}

public sealed class WorkOrderActivityDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid WorkOrderId { get; set; }
    public WorkOrderDTO WorkOrder { get; set; } = null!;
    public WorkOrderActivityTypeEnum Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public bool VisibleToResident { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class IncidentAttachmentDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid? IncidentId { get; set; }
    public IncidentDTO? Incident { get; set; }
    public Guid? WorkOrderId { get; set; }
    public WorkOrderDTO? WorkOrder { get; set; }
    public string MediaReference { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public bool VisibleToResident { get; set; }
    public Guid? UploadedByUserId { get; set; }
    public Guid? UploadedByResidentId { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
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
