using System.ComponentModel.DataAnnotations;

namespace Condotify.Models;

public sealed class MaintenanceDashboardViewModel
{
    public int OpenIncidents { get; set; }
    public int CriticalIncidents { get; set; }
    public int SlaAtRisk { get; set; }
    public int SlaOverdue { get; set; }
    public int OpenWorkOrders { get; set; }
    public int PreventiveDueSoon { get; set; }
    public decimal SlaCompliancePercent { get; set; }
    public List<IncidentViewModel> Incidents { get; set; } = [];
    public List<WorkOrderViewModel> WorkOrders { get; set; } = [];
}

public sealed class WorkOrderViewModel
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid? IncidentId { get; set; }
    public string IncidentCode { get; set; } = string.Empty;
    public Guid? PreventivePlanId { get; set; }
    public string PreventivePlanName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string LocationLabel { get; set; } = string.Empty;
    public string AssignedToName { get; set; } = string.Empty;
    public Guid? ProviderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public DateTime? ScheduledFor { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal ActualCost { get; set; }
    public string CompletionNotes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<WorkOrderChecklistItemViewModel> Checklist { get; set; } = [];
    public List<WorkOrderActivityViewModel> Activities { get; set; } = [];
    public List<IncidentAttachmentViewModel> Attachments { get; set; } = [];
}

public sealed class WorkOrderChecklistItemViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
    public bool IsCompleted { get; set; }
    public string CompletedByName { get; set; } = string.Empty;
    public DateTime? CompletedAt { get; set; }
}

public sealed class WorkOrderActivityViewModel
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public bool VisibleToResident { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class WorkOrderCreateViewModel
{
    public Guid? IncidentId { get; set; }
    [Required, MaxLength(180)] public string Title { get; set; } = string.Empty;
    [MaxLength(4000)] public string Description { get; set; } = string.Empty;
    public int Priority { get; set; } = 1;
    [MaxLength(240)] public string LocationLabel { get; set; } = string.Empty;
    [MaxLength(150)] public string AssignedToName { get; set; } = string.Empty;
    public Guid? ProviderId { get; set; }
    public DateTime? DueAt { get; set; }
    [Range(0, 999999999)] public decimal EstimatedCost { get; set; }
    public List<WorkOrderChecklistInputViewModel> Checklist { get; set; } = [];
}

public sealed class WorkOrderChecklistInputViewModel
{
    [Required, MaxLength(300)] public string Title { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
}

public sealed class WorkOrderStatusUpdateViewModel
{
    public int Status { get; set; }
    [MaxLength(2000)] public string Note { get; set; } = string.Empty;
    public bool VisibleToResident { get; set; } = true;
}

public sealed class WorkOrderAssignmentViewModel
{
    [MaxLength(150)] public string AssignedToName { get; set; } = string.Empty;
    public Guid? ProviderId { get; set; }
}

public sealed class WorkOrderCostUpdateViewModel
{
    [Range(0, 999999999)] public decimal EstimatedCost { get; set; }
    [Range(0, 999999999)] public decimal ActualCost { get; set; }
    [MaxLength(500)] public string Note { get; set; } = string.Empty;
}

public sealed class WorkOrderChecklistUpdateViewModel { public bool IsCompleted { get; set; } }

public sealed class MaintenanceProviderViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int OpenWorkOrders { get; set; }
}

public sealed class MaintenanceProviderFormViewModel
{
    [Required, MaxLength(180)] public string Name { get; set; } = string.Empty;
    [MaxLength(150)] public string Specialty { get; set; } = string.Empty;
    [MaxLength(150)] public string ContactName { get; set; } = string.Empty;
    [MaxLength(40)] public string Phone { get; set; } = string.Empty;
    [EmailAddress, MaxLength(254)] public string Email { get; set; } = string.Empty;
    [MaxLength(1200)] public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class PreventiveMaintenancePlanViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LocationLabel { get; set; } = string.Empty;
    public int IntervalDays { get; set; }
    public int LeadDays { get; set; }
    public DateTime NextDueAt { get; set; }
    public DateTime? LastGeneratedFor { get; set; }
    public Guid? DefaultProviderId { get; set; }
    public string DefaultProviderName { get; set; } = string.Empty;
    public string DefaultAssignedToName { get; set; } = string.Empty;
    public decimal EstimatedCost { get; set; }
    public bool IsActive { get; set; }
    public List<WorkOrderChecklistInputViewModel> Checklist { get; set; } = [];
}

public sealed class PreventiveMaintenancePlanFormViewModel
{
    [Required, MaxLength(180)] public string Name { get; set; } = string.Empty;
    [MaxLength(3000)] public string Description { get; set; } = string.Empty;
    [MaxLength(240)] public string LocationLabel { get; set; } = string.Empty;
    [Range(1, 3650)] public int IntervalDays { get; set; } = 30;
    [Range(0, 365)] public int LeadDays { get; set; } = 3;
    public DateTime NextDueAt { get; set; } = CondotifyTime.Today.AddDays(30);
    public Guid? DefaultProviderId { get; set; }
    [MaxLength(150)] public string DefaultAssignedToName { get; set; } = string.Empty;
    [Range(0, 999999999)] public decimal EstimatedCost { get; set; }
    public bool IsActive { get; set; } = true;
    public List<WorkOrderChecklistInputViewModel> Checklist { get; set; } = [];
}

public sealed class MaintenancePolicyViewModel
{
    [Range(5, 525600)] public int LowResponseMinutes { get; set; } = 1440;
    [Range(5, 525600)] public int LowResolutionMinutes { get; set; } = 10080;
    [Range(5, 525600)] public int MediumResponseMinutes { get; set; } = 480;
    [Range(5, 525600)] public int MediumResolutionMinutes { get; set; } = 4320;
    [Range(5, 525600)] public int HighResponseMinutes { get; set; } = 120;
    [Range(5, 525600)] public int HighResolutionMinutes { get; set; } = 1440;
    [Range(5, 525600)] public int CriticalResponseMinutes { get; set; } = 30;
    [Range(5, 525600)] public int CriticalResolutionMinutes { get; set; } = 240;
}

public sealed class IncidentAttachmentViewModel
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string UploadedByName { get; set; } = string.Empty;
    public bool VisibleToResident { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class IncidentAttachmentUploadViewModel
{
    [Required] public string DataUri { get; set; } = string.Empty;
    [MaxLength(260)] public string FileName { get; set; } = string.Empty;
    [MaxLength(500)] public string Caption { get; set; } = string.Empty;
    public bool VisibleToResident { get; set; } = true;
}

public sealed class ResidentIncidentOverviewViewModel
{
    public int Open { get; set; }
    public int InProgress { get; set; }
    public int Resolved { get; set; }
    public List<IncidentViewModel> Items { get; set; } = [];
}

public sealed class ResidentIncidentCreateViewModel
{
    [Required, MaxLength(180)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(4000)] public string Description { get; set; } = string.Empty;
    public int Category { get; set; } = 6;
    public int Severity { get; set; } = 1;
    [Required, MaxLength(240)] public string LocationLabel { get; set; } = string.Empty;
    public List<IncidentAttachmentUploadViewModel> Photos { get; set; } = [];
}
